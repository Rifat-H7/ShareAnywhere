/*
 * Â© 2026 RH-Factory
 * Author: Md. Zawad Hossain Rifat
 * All rights reserved.
 *
 * This source code is the property of RH-Factory.
 * Unauthorized copying or distribution is prohibited.
 */
using Microsoft.AspNetCore.DataProtection;
using ShareAnywhere.Models;
using ShareAnywhere.Services.Interfaces;
using System.Security.Cryptography;
using System.Threading.Channels;

namespace ShareAnywhere.Services;

public sealed class FileStoreService : IFileStoreService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, FileRecord> _shares = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileRecord> _senders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RelayTransfer> _transfers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NearbyOfferState> _offers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FileRecord> _nearbyTickets = new(StringComparer.Ordinal);
    private readonly string _snippetDirectory;
    private readonly IDataProtector _textProtector;
    private readonly ILogger<FileStoreService> _logger;
    private readonly TimeProvider _clock;

    public FileStoreService(
        IWebHostEnvironment environment,
        IDataProtectionProvider dataProtection,
        ILogger<FileStoreService> logger,
        TimeProvider clock)
    {
        _snippetDirectory = Path.Combine(environment.ContentRootPath, "App_Data", "Snippets");
        _textProtector = dataProtection.CreateProtector("ShareAnywhere.TextSnippets.v1");
        _logger = logger;
        _clock = clock;
    }

    public FileRecord? RegisterFile(FileRegistrationRequest request)
    {
        lock (_gate)
        {
            if (_shares.Count >= ShareLimits.MaxShares)
            {
                return null;
            }

            var record = new FileRecord
            {
                Code = GenerateCode(),
                FileName = request.FileName,
                ContentType = request.ContentType,
                FileSize = request.FileSize,
                SenderToken = GenerateToken(),
                DeleteAfterCount = request.NearbyOnly ? 1 : request.DeleteAfterCount,
                NearbyOnly = request.NearbyOnly,
                ExpiresAt = _clock.GetUtcNow() + ShareLimits.Lifetime
            };

            _shares.Add(record.Code, record);
            _senders.Add(record.SenderToken, record);
            return record;
        }
    }

    public FileRecord? GetFile(string code)
    {
        lock (_gate)
        {
            return _shares.TryGetValue(code, out var record) && IsActive(record) && !record.NearbyOnly
                ? record
                : null;
        }
    }

    public (bool Active, PendingTransferInfo? Transfer) PollSender(string senderToken)
    {
        lock (_gate)
        {
            if (!_senders.TryGetValue(senderToken, out var record))
            {
                return (false, null);
            }

            var transfer = _transfers.Values.FirstOrDefault(transfer => transfer.Share == record && !transfer.Started);
            return (transfer != null || IsActive(record), transfer == null ? null : new PendingTransferInfo
            {
                TransferId = transfer.Id,
                FileName = record.FileName
            });
        }
    }

    public RelayDownloadSession? CreateRelayDownloadSession(string code, bool nearby = false)
    {
        lock (_gate)
        {
            var index = nearby ? _nearbyTickets : _shares;
            if (!index.TryGetValue(code, out var record) || record.NearbyOnly != nearby ||
                record.IsText || !IsActive(record) || record.ActiveDownloads >= record.DeleteAfterCount ||
                _transfers.Count >= ShareLimits.MaxActiveTransfers)
            {
                return null;
            }

            var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(16)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });
            var transfer = new RelayTransfer(GenerateToken(), record, channel);
            _transfers.Add(transfer.Id, transfer);
            record.ActiveDownloads++;

            return new RelayDownloadSession
            {
                TransferId = transfer.Id,
                FileName = record.FileName,
                ContentType = record.ContentType,
                FileSize = record.FileSize,
                Reader = channel.Reader,
                Offer = nearby ? _offers.Values.FirstOrDefault(state => state.Share == record)?.Offer : null
            };
        }
    }

    public RelayUploadSession? GetRelayUploadSession(string senderToken, string transferId)
    {
        lock (_gate)
        {
            if (!_transfers.TryGetValue(transferId, out var transfer) ||
                transfer.Started ||
                !string.Equals(transfer.Share.SenderToken, senderToken, StringComparison.Ordinal))
            {
                return null;
            }

            transfer.Started = true;
            return new RelayUploadSession
            {
                Writer = transfer.Channel.Writer,
                FileSize = transfer.Share.FileSize,
                TransferEnded = transfer.Ended.Token,
                ReceiverResult = transfer.Result.Task
            };
        }
    }

    public void FinishDownload(string transferId, bool success)
    {
        RelayTransfer? transfer;
        lock (_gate)
        {
            if (!_transfers.Remove(transferId, out transfer))
            {
                return;
            }

            var record = transfer.Share;
            record.ActiveDownloads--;
            if (success && --record.DeleteAfterCount == 0)
            {
                RemoveShare(record);
            }

            transfer.Result.TrySetResult(success ? record.DeleteAfterCount : null);
        }

        if (!success)
        {
            transfer.Channel.Writer.TryComplete(new IOException("The download ended before completion."));
            transfer.Ended.Cancel();
        }

        transfer.Ended.Dispose();
    }

    public FileRecord? SaveText(string textContent, int deleteAfterCount)
    {
        lock (_gate)
        {
            if (_shares.Count >= ShareLimits.MaxShares)
            {
                return null;
            }

            var code = GenerateCode();
            var record = new FileRecord
            {
                Code = code,
                FilePath = Path.Combine(_snippetDirectory, $"snippet-{code}.dat"),
                FileName = "text-snippet",
                IsText = true,
                DeleteAfterCount = deleteAfterCount,
                ExpiresAt = _clock.GetUtcNow() + ShareLimits.Lifetime
            };

            Directory.CreateDirectory(_snippetDirectory);
            using (var stream = new FileStream(record.FilePath, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(_textProtector.Protect(textContent));
            }

            _shares.Add(code, record);
            return record;
        }
    }

    public string? ConsumeText(string code)
    {
        lock (_gate)
        {
            if (!_shares.TryGetValue(code, out var record) || !record.IsText || !IsActive(record))
            {
                return null;
            }

            try
            {
                var text = _textProtector.Unprotect(File.ReadAllText(record.FilePath));
                if (--record.DeleteAfterCount == 0)
                {
                    RemoveShare(record);
                }

                return text;
            }
            catch (Exception error) when (error is CryptographicException or FileNotFoundException)
            {
                _logger.LogWarning(error, "A stored snippet could not be read");
                RemoveShare(record);
                return null;
            }
        }
    }

    public NearbyOffer? CreateNearbyOffer(string senderToken, string senderPeerId, string receiverPeerId)
    {
        lock (_gate)
        {
            if (!_senders.TryGetValue(senderToken, out var record) || !record.NearbyOnly || !IsActive(record) ||
                _offers.Values.Any(state => state.Share == record))
            {
                return null;
            }

            var offer = new NearbyOffer(GenerateToken(), senderPeerId, receiverPeerId, record.FileName, record.FileSize);
            _offers.Add(offer.Id, new NearbyOfferState(offer, record));
            return offer;
        }
    }

    public (NearbyOffer Offer, string? Ticket)? RespondToNearbyOffer(string offerId, string receiverPeerId, bool accepted)
    {
        lock (_gate)
        {
            if (!_offers.TryGetValue(offerId, out var state) || state.Offer.ReceiverPeerId != receiverPeerId ||
                state.Ticket != null || !IsActive(state.Share))
            {
                return null;
            }

            if (!accepted)
            {
                RemoveShare(state.Share);
                return (state.Offer, null);
            }

            state.Ticket = GenerateToken();
            _nearbyTickets.Add(state.Ticket, state.Share);
            return (state.Offer, state.Ticket);
        }
    }

    public void RemoveExpiredShares()
    {
        lock (_gate)
        {
            foreach (var record in _shares.Values.Where(record => !IsActive(record) && record.ActiveDownloads == 0).ToArray())
            {
                RemoveShare(record);
            }

            if (!Directory.Exists(_snippetDirectory))
            {
                return;
            }

            var tracked = _shares.Values.Where(record => record.IsText)
                .Select(record => record.FilePath).ToHashSet(StringComparer.Ordinal);
            foreach (var path in Directory.EnumerateFiles(_snippetDirectory, "snippet-*.dat"))
            {
                if (!tracked.Contains(path))
                {
                    DeleteSnippet(path);
                }
            }
        }
    }

    private bool IsActive(FileRecord record) => record.ExpiresAt > _clock.GetUtcNow();

    private void RemoveShare(FileRecord record)
    {
        _shares.Remove(record.Code);
        if (record.IsText)
        {
            DeleteSnippet(record.FilePath);
            return;
        }

        _senders.Remove(record.SenderToken);
        var state = _offers.Values.FirstOrDefault(offer => offer.Share == record);
        if (state != null)
        {
            _offers.Remove(state.Offer.Id);
            if (state.Ticket != null)
            {
                _nearbyTickets.Remove(state.Ticket);
            }
        }
    }

    private void DeleteSnippet(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(error, "A stored snippet could not be removed");
        }
    }

    private string GenerateCode()
    {
        const string alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
        string code;
        do
        {
            code = RandomNumberGenerator.GetString(alphabet, 8);
        } while (_shares.ContainsKey(code));

        return code;
    }

    private static string GenerateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    private sealed record RelayTransfer(string Id, FileRecord Share, Channel<byte[]> Channel)
    {
        public bool Started { get; set; }
        public CancellationTokenSource Ended { get; } = new();
        public TaskCompletionSource<int?> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record NearbyOfferState(NearbyOffer Offer, FileRecord Share)
    {
        public string? Ticket { get; set; }
    }
}
