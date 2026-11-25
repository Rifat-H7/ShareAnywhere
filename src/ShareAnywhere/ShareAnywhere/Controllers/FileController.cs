/*
 * Â© 2026 RH-Factory
 * Author: Md. Zawad Hossain Rifat
 * All rights reserved.
 *
 * This source code is the property of RH-Factory.
 * Unauthorized copying or distribution is prohibited.
 */
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using ShareAnywhere.Hubs;
using ShareAnywhere.Models;
using ShareAnywhere.Services.Interfaces;
using System.Net.Http.Headers;
using System.Text;

namespace ShareAnywhere.Controllers;

public class FileController(
    IFileStoreService fileService,
    IHubContext<NearbyHub> nearbyHub,
    ILogger<FileController> logger) : Controller
{
    [HttpGet]
    public IActionResult Upload() => View();

    [HttpPost]
    [RequestSizeLimit(16 * 1024)]
    [EnableRateLimiting("create-share")]
    public IActionResult RegisterFile([FromBody] FileRegistrationRequest? request)
    {
        if (!ModelState.IsValid || request == null || string.IsNullOrWhiteSpace(request.FileName) ||
            request.FileName.Length > 255 || request.FileName.Any(char.IsControl) ||
            request.FileSize < 0 || request.FileSize > ShareLimits.MaxFileBytes ||
            request.DeleteAfterCount < 1 || request.DeleteAfterCount > ShareLimits.MaxUses)
        {
            return BadRequest("Enter a valid filename, file size, and download limit.");
        }

        request.ContentType = request.ContentType is { Length: <= 255 } &&
            MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            ? mediaType.MediaType ?? "application/octet-stream"
            : "application/octet-stream";

        var record = fileService.RegisterFile(request);
        if (record == null)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, "The server has reached its share limit.");
        }

        Response.Headers.CacheControl = "no-store";
        return Json(new
        {
            code = record.Code,
            fileName = record.FileName,
            senderToken = record.SenderToken,
            expiresAt = record.ExpiresAt,
            downloadLink = record.NearbyOnly ? null : Url.Action(nameof(Download), new { code = record.Code })
        });
    }

    [HttpGet]
    public IActionResult PollTransfer()
    {
        Response.Headers.CacheControl = "no-store";
        var senderToken = ReadSenderToken();
        if (senderToken == null)
        {
            return Unauthorized();
        }

        var (active, transfer) = fileService.PollSender(senderToken);
        if (!active)
        {
            return NotFound();
        }

        return Json(new { hasPending = transfer != null, transferId = transfer?.TransferId });
    }

    [HttpPost]
    [RequestSizeLimit(ShareLimits.MaxFileBytes)]
    public async Task<IActionResult> RelayUploadStream(string transferId)
    {
        var senderToken = ReadSenderToken();
        if (senderToken == null)
        {
            return Unauthorized();
        }

        var session = fileService.GetRelayUploadSession(senderToken, transferId ?? string.Empty);
        if (session == null)
        {
            return NotFound("No pending receiver for this transfer.");
        }

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            HttpContext.RequestAborted, session.TransferEnded);
        var token = cancellation.Token;
        try
        {
            if (Request.ContentLength is long length && length != session.FileSize)
            {
                return BadRequest("The upload size does not match the registered file.");
            }

            long remaining = session.FileSize;
            byte[]? finalChunk = null;
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await Request.Body.ReadAsync(buffer, token)) != 0)
            {
                remaining -= read;
                if (remaining < 0)
                {
                    return BadRequest("The upload size does not match the registered file.");
                }

                var chunk = buffer.AsSpan(0, read).ToArray();
                if (remaining == 0)
                {
                    finalChunk = chunk;
                    continue;
                }

                await session.Writer.WriteAsync(chunk, token);
            }

            if (remaining != 0)
            {
                return BadRequest("The upload size does not match the registered file.");
            }

            if (finalChunk != null)
            {
                await session.Writer.WriteAsync(finalChunk, token);
            }

            session.Writer.TryComplete();
            var remainingDownloads = await session.ReceiverResult.WaitAsync(token);
            return remainingDownloads.HasValue
                ? Ok(new { relayed = true, remainingDownloads })
                : StatusCode(StatusCodes.Status409Conflict, "The receiver disconnected.");
        }
        catch (OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status409Conflict, "The transfer was canceled.");
        }
        catch (Exception error) when (error is IOException or System.Threading.Channels.ChannelClosedException)
        {
            logger.LogDebug(error, "Relay upload ended early");
            return StatusCode(StatusCodes.Status409Conflict, "The transfer ended early.");
        }
        finally
        {
            fileService.FinishDownload(transferId!, success: false);
        }
    }

    [HttpGet]
    public IActionResult UploadResult(string code, string filename)
    {
        Response.Headers.CacheControl = "no-store";
        var record = string.IsNullOrWhiteSpace(code) ? null : fileService.GetFile(code);
        ViewBag.Code = code;
        ViewBag.FileName = filename;
        ViewBag.DownloadLink = Url.Action(nameof(Download), new { code });
        ViewBag.ExpiresAt = record?.ExpiresAt;
        ViewBag.RemainingUses = record?.DeleteAfterCount;
        ViewBag.IsText = record?.IsText == true;
        return View();
    }

    [HttpGet("{code}")]
    [EnableRateLimiting("open-share")]
    public async Task<IActionResult> Download(string code)
    {
        Response.Headers.CacheControl = "no-store";
        var record = fileService.GetFile(code);
        if (record == null)
        {
            return NotFound("Invalid or expired share.");
        }

        if (record.IsText)
        {
            return View("ViewText", record.Code);
        }

        return await StreamDownload(fileService.CreateRelayDownloadSession(code));
    }

    [HttpGet("File/Receive/{ticket}")]
    [EnableRateLimiting("open-share")]
    public Task<IActionResult> Receive(string ticket) =>
        StreamDownload(fileService.CreateRelayDownloadSession(ticket, nearby: true));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(16 * 1024)]
    [EnableRateLimiting("open-share")]
    public IActionResult RevealText(string code)
    {
        Response.Headers.CacheControl = "no-store";
        var text = fileService.ConsumeText(code ?? string.Empty);
        if (text == null)
        {
            return NotFound("This text share is expired or has reached its view limit.");
        }

        ViewBag.TextContent = text;
        return View("ViewText");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(ShareLimits.MaxTextBytes * 4)]
    [EnableRateLimiting("create-share")]
    public IActionResult UploadText(string textContent, int deleteAfterCount = 1)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(textContent) ||
            Encoding.UTF8.GetByteCount(textContent) > ShareLimits.MaxTextBytes ||
            deleteAfterCount < 1 || deleteAfterCount > ShareLimits.MaxUses)
        {
            ViewBag.TextError = "Enter up to 64 KiB of text and use a view limit between 1 and 100.";
            return View("Upload");
        }

        var record = fileService.SaveText(textContent, deleteAfterCount);
        if (record == null)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, "The server has reached its share limit.");
        }

        return RedirectToAction(nameof(UploadResult), new { code = record.Code, filename = record.FileName });
    }

    private async Task<IActionResult> StreamDownload(RelayDownloadSession? session)
    {
        Response.Headers.CacheControl = "no-store";
        if (session == null)
        {
            return NotFound("The share is unavailable or all download slots are in use.");
        }

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
        cancellation.CancelAfter(ShareLimits.TransferTimeout);
        var token = cancellation.Token;
        var success = false;
        var receiving = false;
        try
        {
            Response.ContentType = session.ContentType;
            Response.Headers.ContentDisposition =
                new ContentDispositionHeaderValue("attachment") { FileNameStar = session.FileName }.ToString();
            Response.Headers.XContentTypeOptions = "nosniff";
            Response.ContentLength = session.FileSize;

            await foreach (var chunk in session.Reader.ReadAllAsync(token))
            {
                await Response.Body.WriteAsync(chunk, token);
                if (!receiving && session.Offer is { } offer)
                {
                    receiving = true;
                    await NotifyReceiver(offer, "receiving");
                }
            }

            await Response.CompleteAsync();
            token.ThrowIfCancellationRequested();
            success = true;
            return new EmptyResult();
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (OperationCanceledException)
        {
            return RelayFailure(StatusCodes.Status408RequestTimeout, "The transfer timed out.");
        }
        catch (Exception error)
        {
            logger.LogDebug(error, "Relay download ended early");
            return RelayFailure(StatusCodes.Status502BadGateway, "The sender could not finish the transfer.");
        }
        finally
        {
            fileService.FinishDownload(session.TransferId, success);
            if (session.Offer is { } offer)
            {
                await NotifyReceiver(offer, success ? "complete" : "failed");
            }
        }
    }

    private async Task NotifyReceiver(NearbyOffer offer, string state)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await nearbyHub.Clients.Client(offer.ReceiverPeerId)
                .SendAsync("ReceiveStatus", new { OfferId = offer.Id, State = state }, timeout.Token);
        }
        catch (Exception error)
        {
            logger.LogDebug(error, "Could not send the Nearby download status");
        }
    }

    private IActionResult RelayFailure(int status, string message)
    {
        if (Response.HasStarted)
        {
            HttpContext.Abort();
            return new EmptyResult();
        }

        Response.Clear();
        Response.Headers.CacheControl = "no-store";
        return StatusCode(status, message);
    }

    private string? ReadSenderToken() =>
        AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header) &&
        string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            ? header.Parameter
            : null;
}
