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
        FileRecord? RegisterFile(FileRegistrationRequest request);
        FileRecord? GetFile(string code);
        (bool Active, PendingTransferInfo? Transfer) PollSender(string senderToken);
        RelayDownloadSession? CreateRelayDownloadSession(string code, bool nearby = false);
        RelayUploadSession? GetRelayUploadSession(string senderToken, string transferId);
        void FinishDownload(string transferId, bool success);
        FileRecord? SaveText(string textContent, int deleteAfterCount);
        string? ConsumeText(string code);
        NearbyOffer? CreateNearbyOffer(string senderToken, string senderPeerId, string receiverPeerId);
        (NearbyOffer Offer, string? Ticket)? RespondToNearbyOffer(string offerId, string receiverPeerId, bool accepted);
        void RemoveExpiredShares();
    }
}
