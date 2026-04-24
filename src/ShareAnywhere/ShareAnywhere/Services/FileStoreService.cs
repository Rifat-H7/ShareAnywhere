/*
 * © 2026 RH-Factory
 * Author: Md. Zawad Hossain Rifat
 * All rights reserved.
 *
 * This source code is the property of RH-Factory.
 * Unauthorized copying or distribution is prohibited.
 */
using ShareAnywhere.Models;
using ShareAnywhere.Services.Interfaces;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace ShareAnywhere.Services
{
    public class FileStoreService : IFileStoreService
    {
        private readonly IWebHostEnvironment _env;
        private readonly ConcurrentDictionary<string, FileRecord> _fileMap = new();
        private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _pendingTransfersBySender = new();
        private readonly ConcurrentDictionary<string, RelayTransfer> _relayTransfers = new();

        public FileStoreService(IWebHostEnvironment env)
        {
            _env = env;
        }

        public FileRecord RegisterFile(FileRegistrationRequest request)
        {
            var code = GenerateUniqueCode();
            var senderToken = GenerateSenderToken();

            var record = new FileRecord
            {
                Code = code,
                FileName = request.FileName,
                IsText = false,
                ContentType = string.IsNullOrWhiteSpace(request.ContentType) ? "application/octet-stream" : request.ContentType,
                FileSize = Math.Max(0, request.FileSize),
                SenderToken = senderToken,
                DeleteAfterCount = Math.Max(1, request.DeleteAfterCount)
            };

            _fileMap[code] = record;
            _pendingTransfersBySender[senderToken] = new ConcurrentQueue<string>();

            return record;
        }

        public FileRecord? GetFile(string code)
        {
            if (_fileMap.TryGetValue(code.ToUpperInvariant(), out var record))
            {
                return record;
            }

            return null;
        }

        public PendingTransferInfo? GetPendingTransfer(string senderToken)
        {
            if (!_pendingTransfersBySender.TryGetValue(senderToken, out var queue))
            {
                return null;
            }

            while (queue.TryDequeue(out var transferId))
            {
                if (_relayTransfers.TryGetValue(transferId, out var transfer))
                {
                    return new PendingTransferInfo
                    {
                        TransferId = transferId,
                        FileName = transfer.FileName
                    };
                }
            }

            return null;
        }

        public RelayDownloadSession? CreateRelayDownloadSession(string code)
        {
            if (!_fileMap.TryGetValue(code.ToUpperInvariant(), out var record))
            {
                return null;
            }

            if (record.IsText)
            {
                return null;
            }

            if (record.DeleteAfterCount <= 0)
            {
                return null;
            }

            var transferId = Guid.NewGuid().ToString("N");
            var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(16)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });

            var transfer = new RelayTransfer
            {
                TransferId = transferId,
                Code = record.Code,
                SenderToken = record.SenderToken,
                FileName = record.FileName,
                Writer = channel.Writer,
                Reader = channel.Reader
            };

            _relayTransfers[transferId] = transfer;

            if (!_pendingTransfersBySender.TryGetValue(record.SenderToken, out var queue))
            {
                queue = new ConcurrentQueue<string>();
                _pendingTransfersBySender[record.SenderToken] = queue;
            }

            queue.Enqueue(transferId);

            return new RelayDownloadSession
            {
                TransferId = transferId,
                FileName = record.FileName,
                ContentType = record.ContentType,
                FileSize = record.FileSize,
                Reader = channel.Reader
            };
        }

        public RelayUploadSession? GetRelayUploadSession(string senderToken, string transferId)
        {
            if (!_relayTransfers.TryGetValue(transferId, out var transfer))
            {
                return null;
            }

            if (!string.Equals(transfer.SenderToken, senderToken, StringComparison.Ordinal))
            {
                return null;
            }

            if (Interlocked.CompareExchange(ref transfer.Started, 1, 0) != 0)
            {
                return null;
            }

            return new RelayUploadSession
            {
                Writer = transfer.Writer
            };
        }

        public void CompleteRelayUpload(string transferId, bool success, Exception? error = null)
        {
            if (!_relayTransfers.TryRemove(transferId, out var transfer))
            {
                return;
            }

            if (success)
            {
                transfer.Writer.TryComplete();

                if (_fileMap.TryGetValue(transfer.Code, out var record))
                {
                    lock (record)
                    {
                        record.DeleteAfterCount--;
                        if (record.DeleteAfterCount <= 0)
                        {
                            DeleteFile(record.Code);
                        }
                    }
                }

                return;
            }

            transfer.Writer.TryComplete(error ?? new IOException("Transfer failed."));
        }

        public void DeleteFile(string code)
        {
            if (_fileMap.TryRemove(code.ToUpperInvariant(), out var record))
            {
                if (record.IsText && File.Exists(record.FilePath))
                {
                    File.Delete(record.FilePath);
                }

                if (!string.IsNullOrWhiteSpace(record.SenderToken))
                {
                    _pendingTransfersBySender.TryRemove(record.SenderToken, out _);
                }
            }
        }

        private string GenerateUniqueCode()
        {
            string code;
            do
            {
                code = Path.GetRandomFileName().Replace(".", "").Substring(0, 6).ToUpperInvariant();
            } while (_fileMap.ContainsKey(code));

            return code;
        }

        private static string GenerateSenderToken()
        {
            return Guid.NewGuid().ToString("N");
        }

        public FileRecord? SaveText(string textContent, int deleteAfterCount)
        {
            // Generate a unique file name
            var code = GenerateUniqueCode();
            var fileName = $"{code}.txt";

            // Define the path where the file will be saved
            string folderPath = Path.Combine(_env.WebRootPath, "Uploads");
            Directory.CreateDirectory(folderPath); // Ensure folder exists

            string filePath = Path.Combine(folderPath, fileName);

            // Save the text to the file
            File.WriteAllText(filePath, textContent);


            var record = new FileRecord
            {
                Code = code,
                FilePath = filePath,
                FileName = fileName,
                IsText = true,
                DeleteAfterCount = deleteAfterCount
            };

            _fileMap[code] = record;
            return record;
        }

        public FileRecord? GetText(string code)
        {
            if (_fileMap.TryGetValue(code.ToUpperInvariant(), out var record))
            {
                if (!File.Exists(record.FilePath))
                {
                    return null;
                }

                record.Text = File.ReadAllText(record.FilePath);
                return record;
            }

            return null;
        }

        private class RelayTransfer
        {
            public required string TransferId { get; set; }
            public required string Code { get; set; }
            public required string SenderToken { get; set; }
            public required string FileName { get; set; }
            public required ChannelWriter<byte[]> Writer { get; set; }
            public required ChannelReader<byte[]> Reader { get; set; }
            public int Started;
        }
    }
}
