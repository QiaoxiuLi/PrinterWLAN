using System.Text.Json.Serialization;

namespace PrinterWLAN.Models;

public sealed class PrinterWlanOptions
{
    public int Port { get; set; } = 8080;
    public long MaxUploadBytes { get; set; } = 100L * 1024 * 1024;
    public int TempRetentionMinutes { get; set; } = 60;
    public int WordConversionTimeoutSeconds { get; set; } = 60;
    public long LogMaxBytes { get; set; } = 40L * 1024 * 1024 * 1024;
    public long LogCleanupTargetBytes { get; set; } = 38L * 1024 * 1024 * 1024;
    public string LibreOfficePath { get; set; } = "third-party/LibreOffice/program/soffice.exe";
    public bool UseFakePrinter { get; set; }
}

public sealed record UserRecord(long Id, string Username, string NormalizedUsername, string PasswordHash,
    byte[] EncryptedPassword, long Sequence, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record ImportResult(int Added, int Updated, int Ignored);

public sealed record DocumentRecord(string Id, long UserId, string OriginalFilename, string Extension,
    string DetectedMime, long FileSize, DateTimeOffset? ClientLastModified, DateTimeOffset? DocumentCreatedAt,
    DateTimeOffset? DocumentModifiedAt, DateTimeOffset ServerReceivedAt, DateTimeOffset? PreviewAt,
    long ConversionDurationMs, int TotalPages, string SourcePath, string PdfPath, string Status);

public sealed class PrintRequest
{
    public required string DocumentId { get; set; }
    public required string PrinterId { get; set; }
    public required string PrinterName { get; set; }
    public required string PaperSize { get; set; }
    public string Orientation { get; set; } = "portrait";
    public string Duplex { get; set; } = "simplex";
    public string PageRange { get; set; } = "all";
    public int Copies { get; set; } = 1;
    public bool Collate { get; set; } = true;
    public string ColorMode { get; set; } = "color";
    public string? PaperSource { get; set; }
    public string? Resolution { get; set; }
    public string ScaleMode { get; set; } = "fit";
    public int ScalePercent { get; set; } = 100;
    public bool Center { get; set; } = true;
    [JsonIgnore] public IReadOnlyList<int> SelectedPages { get; set; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record class PrintSubmissionRequest
{
    public required string DocumentId { get; set; }
    public required string PaperSize { get; set; }
    public string Orientation { get; set; } = "portrait";
    public string Duplex { get; set; } = "simplex";
    public string PageRange { get; set; } = "all";
    public int Copies { get; set; } = 1;
    public bool Collate { get; set; } = true;
    public string ColorMode { get; set; } = "color";
    public string? PaperSource { get; set; }
    public string? Resolution { get; set; }
    public string ScaleMode { get; set; } = "fit";
    public int ScalePercent { get; set; } = 100;
    public bool Center { get; set; } = true;
}

public sealed record PrintJobRecord(string Id, long UserId, string Username, string DocumentId,
    string Status, string SettingsJson, DateTimeOffset SubmittedAt, DateTimeOffset? ProcessingStartedAt,
    DateTimeOffset? SentAt, DateTimeOffset? FailedAt, string? FriendlyError, string? InternalError,
    string SessionId, string? DeviceId, string? IpAddress);

public sealed record PaperCapability(string Name, int RawKind, int Width, int Height);
public sealed record SourceCapability(string Name, int RawKind);
public sealed record ResolutionCapability(string Name, int X, int Y, int RawKind);
public sealed record PrinterCapability(string Id, string Name, bool IsDefault, bool IsValid, string Status, bool SupportsColor,
    bool CanDuplex, int MaximumCopies, bool SupportsCollate, IReadOnlyList<PaperCapability> PaperSizes,
    IReadOnlyList<SourceCapability> PaperSources, IReadOnlyList<ResolutionCapability> Resolutions);

public sealed record PrinterSelectionState(string? SelectedPrinterId, string? SelectedPrinterName,
    string Status, PrinterCapability? SelectedPrinter, IReadOnlyList<PrinterCapability> Printers);

public sealed record ActivityRecord(DateTimeOffset UtcTime, DateTimeOffset LocalTime, string Type,
    long? UserId, string? Username, string? SessionId, string? DeviceId, string? IpAddress,
    string? UserAgent, string? Language, string? Platform, string? Viewport, string? Screen,
    string? Timezone, string? Referrer, string? DetailJson);

public sealed record SliceInfo(string Name, DateOnly Start, DateOnly End, string State, long Bytes);
