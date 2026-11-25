namespace ShareAnywhere.Models;

public sealed record NearbyOffer(
    string Id,
    string SenderPeerId,
    string ReceiverPeerId,
    string FileName,
    long FileSize);
