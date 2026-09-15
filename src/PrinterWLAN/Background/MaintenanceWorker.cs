using PrinterWLAN.Documents;
using PrinterWLAN.Logging;

namespace PrinterWLAN.Background;

public sealed class MaintenanceWorker(DocumentService documents, ActivityLogService activityLogs,
    ILogger<MaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken)) await RunAsync(stoppingToken);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await documents.CleanupExpiredAsync(cancellationToken);
            await activityLogs.MaintainAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { logger.LogError(exception, "Scheduled maintenance failed"); }
    }
}
