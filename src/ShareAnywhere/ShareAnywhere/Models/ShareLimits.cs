namespace ShareAnywhere.Models;

public static class ShareLimits
{
    public const long MaxFileBytes = 1024L * 1024 * 1024;
    public const int MaxTextBytes = 64 * 1024;
    public const int MaxUses = 100;
    public const int MaxShares = 256;
    public const int MaxActiveTransfers = 32;

    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan TransferTimeout = TimeSpan.FromMinutes(10);
}
