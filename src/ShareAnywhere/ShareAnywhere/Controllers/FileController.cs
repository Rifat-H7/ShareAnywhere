/*
 * © 2026 RH-Factory
 * Author: Md. Zawad Hossain Rifat
 * All rights reserved.
 *
 * This source code is the property of RH-Factory.
 * Unauthorized copying or distribution is prohibited.
 */
using Microsoft.AspNetCore.Mvc;
using ShareAnywhere.Models;
using ShareAnywhere.Services.Interfaces;
using System.Buffers;
using System.Net.Http.Headers;

namespace ShareAnywhere.Controllers
{
    public class FileController : Controller
    {
        private readonly IFileStoreService _fileService;

        public FileController(IFileStoreService fileService)
        {
            _fileService = fileService;
        }

        [HttpGet]
        public IActionResult Upload()
        {
            return View();
        }

        [HttpPost]
        public IActionResult RegisterFile([FromBody] FileRegistrationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.FileName))
            {
                return BadRequest("File name is required.");
            }

            var record = _fileService.RegisterFile(request);
            var downloadLink = Url.Action("Download", "File", new { code = record.Code }, Request.Scheme, Request.Host.ToString());

            return Json(new
            {
                code = record.Code,
                fileName = record.FileName,
                senderToken = record.SenderToken,
                downloadLink
            });
        }

        [HttpGet]
        public IActionResult PollTransfer(string senderToken)
        {
            if (string.IsNullOrWhiteSpace(senderToken))
            {
                return BadRequest("Sender token is required.");
            }

            var pending = _fileService.GetPendingTransfer(senderToken);
            if (pending == null)
            {
                return Json(new { hasPending = false });
            }

            return Json(new
            {
                hasPending = true,
                transferId = pending.TransferId,
                fileName = pending.FileName
            });
        }

        [HttpPost]
        [RequestSizeLimit(long.MaxValue)]
        public async Task<IActionResult> RelayUploadStream(string senderToken, string transferId)
        {
            var relayUploadSession = _fileService.GetRelayUploadSession(senderToken, transferId);
            if (relayUploadSession == null)
            {
                return NotFound("No pending receiver for this transfer.");
            }

            var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            try
            {
                int read;
                while ((read = await Request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), HttpContext.RequestAborted)) > 0)
                {
                    var chunk = new byte[read];
                    Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                    await relayUploadSession.Writer.WriteAsync(chunk, HttpContext.RequestAborted);
                }

                _fileService.CompleteRelayUpload(transferId, success: true);
                return Ok(new { relayed = true });
            }
            catch (Exception ex)
            {
                _fileService.CompleteRelayUpload(transferId, success: false, error: ex);
                return StatusCode(StatusCodes.Status500InternalServerError, "Transfer failed.");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        [HttpGet]
        public IActionResult UploadResult(string code, string filename)
        {
            ViewBag.Code = code;
            ViewBag.FileName = filename;
            ViewBag.DownloadLink = Url.Action("Download", "File", new { code }, Request.Scheme, Request.Host.ToString());
            return View();
        }

        [HttpGet("{code}")]
        public async Task<IActionResult> Download(string code)
        {
            var record = _fileService.GetFile(code);
            if (record == null) return NotFound("Invalid download code.");

            if (record.IsText)
            {
                var textRecord = _fileService.GetText(code);
                if (textRecord == null)
                {
                    return NotFound("Text content is unavailable.");
                }

                lock (textRecord)
                {
                    textRecord.DeleteAfterCount--;

                    if (textRecord.DeleteAfterCount <= 0)
                    {
                        _fileService.DeleteFile(code);
                    }
                }

                ViewBag.TextContent = textRecord.Text;
                return View("ViewText");
            }

            var relaySession = _fileService.CreateRelayDownloadSession(code);
            if (relaySession == null)
            {
                return NotFound("Sender is unavailable or transfer limit reached.");
            }

            Response.ContentType = relaySession.ContentType;
            Response.Headers.ContentDisposition =
                new ContentDispositionHeaderValue("attachment") { FileNameStar = relaySession.FileName }.ToString();
            Response.Headers.CacheControl = "no-store";
            if (relaySession.FileSize > 0)
            {
                Response.ContentLength = relaySession.FileSize;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(10));

            try
            {
                await foreach (var chunk in relaySession.Reader.ReadAllAsync(timeoutCts.Token))
                {
                    await Response.Body.WriteAsync(chunk, timeoutCts.Token);
                }

                return new EmptyResult();
            }
            catch (OperationCanceledException)
            {
                _fileService.CompleteRelayUpload(relaySession.TransferId, success: false, error: new TimeoutException("Transfer timed out."));

                if (HttpContext.RequestAborted.IsCancellationRequested)
                {
                    return new EmptyResult();
                }

                return StatusCode(StatusCodes.Status408RequestTimeout, "Sender did not start transfer in time.");
            }
            catch (Exception ex)
            {
                _fileService.CompleteRelayUpload(relaySession.TransferId, success: false, error: ex);
                return StatusCode(StatusCodes.Status500InternalServerError, "Relay transfer failed.");
            }
        }

        [HttpPost]
        public IActionResult UploadText(string textContent, int deleteAfterCount = 1)
        {
            if (string.IsNullOrWhiteSpace(textContent))
            {
                ModelState.AddModelError("", "Please enter text.");
                return View("Upload");
            }

            var record = _fileService.SaveText(textContent, deleteAfterCount);
            if (record == null)
            {
                return BadRequest("Unable to store text.");
            }

            return RedirectToAction("UploadResult", new { code = record.Code, filename = "text-snippet" });
        }
    }
}
