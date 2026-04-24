/*
 * © 2026 RH-Factory
 * Author: Md. Zawad Hossain Rifat
 * All rights reserved.
 *
 * This source code is the property of RH-Factory.
 * Unauthorized copying or distribution is prohibited.
 */
using ShareAnywhere.Models;

namespace ShareAnywhere.Services.Interfaces
{
    public interface IFileStoreService
    {
        FileRecord RegisterFile(FileRegistrationRequest request);
        FileRecord? GetFile(string code);
        PendingTransferInfo? GetPendingTransfer(string senderToken);
        RelayDownloadSession? CreateRelayDownloadSession(string code);
        RelayUploadSession? GetRelayUploadSession(string senderToken, string transferId);
        void CompleteRelayUpload(string transferId, bool success, Exception? error = null);
        void DeleteFile(string code);
        FileRecord? SaveText(string textContent, int deleteAfterCount);
        FileRecord? GetText(string code);
    }
}
