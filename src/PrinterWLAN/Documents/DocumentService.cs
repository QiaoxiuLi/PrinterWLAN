using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Options;
using PDFtoImage;
using PrinterWLAN.Models;
using PrinterWLAN.Storage;

namespace PrinterWLAN.Documents;

public sealed class DocumentService(AppPaths paths, AppDatabase database, IWordConverter converter,
    IOptions<PrinterWlanOptions> options, ILogger<DocumentService> logger)
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".doc", ".docx" };

    public async Task<DocumentRecord> ReceiveAsync(long userId, IFormFile file, DateTimeOffset? clientLastModified,
        CancellationToken cancellationToken)
    {
        if (file.Length <= 0) throw new DocumentException("请选择一个有效的文件。", "Empty upload rejected.");
        if (file.Length > options.Value.MaxUploadBytes) throw new DocumentException("文件太大，请选择较小的文件。", "Upload exceeded configured limit.");
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension)) throw new DocumentException("仅支持 PDF、DOC 和 DOCX 文件。", "Unsupported extension.");

        var id = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(paths.Temp, id);
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source" + extension);
        try
        {
            await using (var output = new FileStream(sourcePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await file.CopyToAsync(output, cancellationToken);
            var detectedMime = await ValidateSignatureAsync(sourcePath, extension, cancellationToken);
            var (created, modified) = extension == ".docx" ? ReadDocxProperties(sourcePath) : (null, null);
            var pdfPath = extension == ".pdf" ? sourcePath : await converter.ConvertToPdfAsync(sourcePath, directory, cancellationToken);
            int pageCount;
            try { pageCount = Conversion.GetPageCount(pdfPath); }
            catch (Exception exception) { throw new DocumentException("无法打开这个文件，请确认文件没有损坏或加密。", "PDFium could not read document.", exception); }
            if (pageCount <= 0) throw new DocumentException("这个文件没有可预览的页面。", "Document has no pages.");
            var received = DateTimeOffset.UtcNow;
            var record = new DocumentRecord(id, userId, Path.GetFileName(file.FileName), extension, detectedMime,
                file.Length, clientLastModified, created, modified, received, pageCount, sourcePath, pdfPath, "ready");
            await database.SaveDocumentAsync(record, cancellationToken);
            return record;
        }
        catch
        {
            SafeDeleteDirectory(directory);
            throw;
        }
    }

    public async Task DeleteAsync(DocumentRecord document, CancellationToken cancellationToken = default)
    {
        SafeDeleteDirectory(Path.GetDirectoryName(document.SourcePath)!);
        await database.DeleteDocumentRowAsync(document.Id, cancellationToken);
    }

    public async Task CleanupExpiredAsync(CancellationToken cancellationToken)
    {
        var before = DateTimeOffset.UtcNow.AddMinutes(-options.Value.TempRetentionMinutes);
        foreach (var document in await database.GetExpiredDocumentsAsync(before, cancellationToken))
        {
            try { await DeleteAsync(document, cancellationToken); }
            catch (Exception exception) { logger.LogWarning(exception, "Could not remove expired document {DocumentId}", document.Id); }
        }
        foreach (var directory in Directory.EnumerateDirectories(paths.Temp))
        {
            if (string.Equals(Path.GetFileName(directory), "Exports", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                if (Directory.GetLastWriteTimeUtc(directory) < before.UtcDateTime) SafeDeleteDirectory(directory);
            }
            catch (Exception exception) { logger.LogDebug(exception, "Could not inspect orphan temp directory."); }
        }
        foreach (var export in Directory.EnumerateFiles(paths.Exports))
        {
            try { if (File.GetLastWriteTimeUtc(export) < before.UtcDateTime) File.Delete(export); }
            catch (Exception exception) { logger.LogDebug(exception, "Could not remove expired export."); }
        }
    }

    public static async Task<string> ValidateSignatureAsync(string path, string extension, CancellationToken cancellationToken = default)
    {
        var signature = new byte[8];
        await using var stream = File.OpenRead(path);
        var read = await stream.ReadAsync(signature, cancellationToken);
        if (extension == ".pdf" && read >= 5 && signature.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) return "application/pdf";
        if (extension == ".docx" && read >= 4 && signature.AsSpan(0, 4).SequenceEqual(new byte[] { 0x50, 0x4B, 0x03, 0x04 }))
        {
            try { using var document = WordprocessingDocument.Open(path, false); _ = document.MainDocumentPart ?? throw new InvalidDataException(); }
            catch (Exception exception) { throw new DocumentException("无法打开这个 Word 文件，请确认文件没有损坏。", "Invalid DOCX package.", exception); }
            return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        }
        if (extension == ".doc" && read >= 8 && signature.AsSpan(0, 8).SequenceEqual(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }))
            return "application/msword";
        throw new DocumentException("文件类型与扩展名不一致，请选择正确的文件。", "File signature mismatch.");
    }

    private static (DateTimeOffset? Created, DateTimeOffset? Modified) ReadDocxProperties(string path)
    {
        try
        {
            using var document = WordprocessingDocument.Open(path, false);
            var properties = document.PackageProperties;
            return (properties.Created is null ? null : new DateTimeOffset(DateTime.SpecifyKind(properties.Created.Value, DateTimeKind.Utc)),
                properties.Modified is null ? null : new DateTimeOffset(DateTime.SpecifyKind(properties.Modified.Value, DateTimeKind.Utc)));
        }
        catch (Exception exception) when (exception is not DocumentException)
        {
            throw new DocumentException("无法读取这个 Word 文件，请确认文件没有损坏。", "DOCX metadata read failed.", exception);
        }
    }

    private static void SafeDeleteDirectory(string directory)
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
