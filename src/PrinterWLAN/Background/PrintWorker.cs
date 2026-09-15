using System.Text.Json;
using PrinterWLAN.Documents;
using PrinterWLAN.Logging;
using PrinterWLAN.Models;
using PrinterWLAN.Printing;
using PrinterWLAN.Storage;

namespace PrinterWLAN.Background;

public sealed class PrintWorker(PrintJobQueue queue, AppDatabase database, IPrinterService printer,
    DocumentService documents, ActivityLogService activityLogs, ILogger<PrintWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var jobId in queue.ReadAllAsync(stoppingToken))
        {
            try { await ProcessAsync(jobId, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Unexpected print worker failure for {JobId}", jobId); }
        }
    }

    private async Task ProcessAsync(string jobId, CancellationToken cancellationToken)
    {
        var job = await database.GetJobAsync(jobId, cancellationToken: cancellationToken);
        if (job is null || job.Status != "queued") return;
        var document = await database.GetDocumentAsync(job.DocumentId, job.UserId, cancellationToken);
        if (document is null)
        {
            await database.UpdateJobAsync(jobId, "failed", "临时文件已过期，请重新上传。", "Document row missing.", cancellationToken);
            return;
        }
        await database.UpdateJobAsync(jobId, "processing", cancellationToken: cancellationToken);
        var request = JsonSerializer.Deserialize<PrintRequest>(job.SettingsJson) ?? throw new InvalidDataException("Invalid persisted print settings.");
        request.SelectedPages = PageRangeParser.Parse(request.PageRange, document.TotalPages);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await printer.SubmitAsync(document, request, jobId, cancellationToken);
            stopwatch.Stop();
            await database.UpdateJobAsync(jobId, "sent", cancellationToken: cancellationToken);
            var completedJob = await database.GetJobAsync(jobId, cancellationToken: cancellationToken) ?? job;
            await activityLogs.WriteAsync(BuildRecord(completedJob, "print_sent", document, request, stopwatch.Elapsed, null), cancellationToken);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            var friendly = exception is PrintValidationException validation ? validation.Message : "打印任务未能发送，请检查打印机后重试。";
            var internalError = Sanitize(exception);
            await database.UpdateJobAsync(jobId, "failed", friendly, internalError, cancellationToken);
            var failedJob = await database.GetJobAsync(jobId, cancellationToken: cancellationToken) ?? job;
            await activityLogs.WriteAsync(BuildRecord(failedJob, "print_failed", document, request, stopwatch.Elapsed, internalError), cancellationToken);
            logger.LogError(exception, "Print job {JobId} failed", jobId);
        }
        finally
        {
            try { await documents.DeleteAsync(document, cancellationToken); }
            catch (Exception exception) { logger.LogWarning(exception, "Could not clean temporary document for job {JobId}", jobId); }
        }
    }

    private static ActivityRecord BuildRecord(PrintJobRecord job, string type, DocumentRecord document, PrintRequest request,
        TimeSpan duration, string? error)
    {
        var now = DateTimeOffset.Now;
        var detail = JsonSerializer.Serialize(new
        {
            jobId = job.Id, document.OriginalFilename, document.Extension, document.DetectedMime, document.FileSize,
            document.ClientLastModified, document.DocumentCreatedAt, document.DocumentModifiedAt, document.TotalPages,
            request.PrinterName, request.PaperSize, request.Orientation, request.ColorMode, request.Duplex, request.PageRange,
            selectedPageCount = request.SelectedPages.Count, request.Copies,
            totalRequestedPages = request.SelectedPages.Count * request.Copies, request.Collate, request.PaperSource,
            request.Resolution, request.ScaleMode, request.ScalePercent, request.Center,
            uploadAt = document.ServerReceivedAt, document.PreviewAt, job.SubmittedAt, job.ProcessingStartedAt,
            job.SentAt, job.FailedAt, document.ConversionDurationMs,
            printSubmissionDurationMs = (long)duration.TotalMilliseconds, error
        });
        return new ActivityRecord(now.ToUniversalTime(), now, type, job.UserId, job.Username, job.SessionId, job.DeviceId,
            job.IpAddress, null, null, null, null, null, null, null, detail);
    }
    private static string Sanitize(Exception exception)
    {
        var value = $"{exception.GetType().Name}: {exception.Message}";
        return value.Length <= 2000 ? value : value[..2000];
    }
}
