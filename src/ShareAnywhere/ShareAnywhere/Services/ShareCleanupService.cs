using ShareAnywhere.Services.Interfaces;

namespace ShareAnywhere.Services;

public sealed class ShareCleanupService(IFileStoreService files, ILogger<ShareCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                files.RemoveExpiredShares();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(error, "Expired share cleanup could not finish");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
