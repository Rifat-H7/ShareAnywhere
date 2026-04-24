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
