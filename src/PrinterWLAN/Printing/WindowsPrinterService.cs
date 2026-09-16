using System.Drawing;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using PDFtoImage;
using PrinterWLAN.Models;
using SkiaSharp;

namespace PrinterWLAN.Printing;

public sealed class WindowsPrinterService(ILogger<WindowsPrinterService> logger) : IPrinterService
{
    public Task<IReadOnlyList<PrinterCapability>> GetPrintersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new List<PrinterCapability>();
        foreach (string name in PrinterSettings.InstalledPrinters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { result.Add(ReadCapability(name)); }
            catch (Exception exception) { logger.LogWarning(exception, "Could not read capabilities for printer {PrinterName}", name); }
        }
        return Task.FromResult<IReadOnlyList<PrinterCapability>>(result);
    }

    public async Task<PrinterCapability> ValidateAsync(PrintRequest request, CancellationToken cancellationToken = default)
    {
        var printer = (await GetPrintersAsync(cancellationToken)).FirstOrDefault(p =>
            p.Id.Equals(request.PrinterId, StringComparison.Ordinal) &&
            p.Name.Equals(request.PrinterName, StringComparison.Ordinal));
        if (printer is null || !printer.IsValid || printer.Status is "offline" or "unavailable")
            throw new PrintValidationException("管理员设置的打印机当前不可用，请联系管理员。");
        if (!printer.PaperSizes.Any(p => p.Name.Equals(request.PaperSize, StringComparison.Ordinal))) throw new PrintValidationException("所选纸张大小已不可用，请重新选择。");
        if (!request.Duplex.Equals("simplex", StringComparison.OrdinalIgnoreCase) && !printer.CanDuplex) throw new PrintValidationException("这台打印机不支持双面打印。");
        if (request.ColorMode.Equals("color", StringComparison.OrdinalIgnoreCase) && !printer.SupportsColor) throw new PrintValidationException("这台打印机不支持彩色打印。");
        if (request.Copies < 1 || request.Copies > Math.Max(1, printer.MaximumCopies)) throw new PrintValidationException("打印份数超出打印机支持范围。");
        if (request.Collate && request.Copies > 1 && !printer.SupportsCollate) throw new PrintValidationException("这台打印机不支持逐份打印。");
        if (request.PaperSource is not null && !printer.PaperSources.Any(p => p.Name.Equals(request.PaperSource, StringComparison.Ordinal))) throw new PrintValidationException("所选纸盒已不可用，请重新选择。");
        if (request.Resolution is not null && !printer.Resolutions.Any(p => ResolutionKey(p).Equals(request.Resolution, StringComparison.Ordinal))) throw new PrintValidationException("所选打印质量已不可用，请重新选择。");
        if (request.Orientation is not ("portrait" or "landscape")) throw new PrintValidationException("页面方向无效。");
        if (request.Duplex is not ("simplex" or "long-edge" or "short-edge")) throw new PrintValidationException("双面设置无效。");
        if (request.ScaleMode is not ("fit" or "actual" or "custom")) throw new PrintValidationException("缩放设置无效。");
        if (request.ScaleMode == "custom" && request.ScalePercent is < 25 or > 200) throw new PrintValidationException("自定义比例必须在 25% 到 200% 之间。");
        return printer;
    }

    public async Task SubmitAsync(DocumentRecord document, PrintRequest request, string jobId, CancellationToken cancellationToken)
    {
        var capability = await ValidateAsync(request, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        using var printDocument = new PrintDocument
        {
            DocumentName = $"PrinterWLAN-{jobId}-{SafeName(document.OriginalFilename)}",
            PrintController = new StandardPrintController()
        };
        var settings = printDocument.PrinterSettings;
        settings.PrinterName = capability.Name;
        settings.Copies = checked((short)request.Copies);
        settings.Collate = request.Collate && request.Copies > 1;
        settings.Duplex = request.Duplex switch
        {
            "long-edge" => Duplex.Vertical,
            "short-edge" => Duplex.Horizontal,
            _ => Duplex.Simplex
        };
        printDocument.DefaultPageSettings.Landscape = request.Orientation == "landscape";
        printDocument.DefaultPageSettings.Color = request.ColorMode == "color";
        printDocument.DefaultPageSettings.PaperSize = settings.PaperSizes.Cast<PaperSize>().First(p => p.PaperName == request.PaperSize);
        if (request.PaperSource is not null)
            printDocument.DefaultPageSettings.PaperSource = settings.PaperSources.Cast<PaperSource>().First(p => p.SourceName == request.PaperSource);
        if (request.Resolution is not null)
            printDocument.DefaultPageSettings.PrinterResolution = settings.PrinterResolutions.Cast<PrinterResolution>().First(p => ResolutionKey(p) == request.Resolution);

        var pageIndex = 0;
        printDocument.PrintPage += (_, args) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var renderInput = File.OpenRead(document.PdfPath);
            using var bitmap = Conversion.ToImage(renderInput, request.SelectedPages[pageIndex] - 1);
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = encoded.AsStream();
            using var image = Image.FromStream(stream);
            using var sizeInput = File.OpenRead(document.PdfPath);
            var pdfSize = Conversion.GetPageSize(sizeInput, request.SelectedPages[pageIndex] - 1);
            var actualWidth = Math.Max(1, (int)Math.Round(pdfSize.Width / 72d * 100d));
            var actualHeight = Math.Max(1, (int)Math.Round(pdfSize.Height / 72d * 100d));
            var target = CalculateTarget(args.MarginBounds, image.Width, image.Height, request, actualWidth, actualHeight);
            args.Graphics?.DrawImage(image, target);
            pageIndex++;
            args.HasMorePages = pageIndex < request.SelectedPages.Count;
        };
        await Task.Run(printDocument.Print, cancellationToken);
    }

    public static Rectangle CalculateTarget(Rectangle bounds, int sourceWidth, int sourceHeight, PrintRequest request,
        int? actualWidth = null, int? actualHeight = null)
    {
        var baseWidth = actualWidth ?? sourceWidth;
        var baseHeight = actualHeight ?? sourceHeight;
        double scale = request.ScaleMode switch
        {
            "actual" => 1,
            "custom" => request.ScalePercent / 100d,
            _ => Math.Min(bounds.Width / (double)baseWidth, bounds.Height / (double)baseHeight)
        };
        var width = Math.Max(1, (int)Math.Round(baseWidth * scale));
        var height = Math.Max(1, (int)Math.Round(baseHeight * scale));
        var x = request.Center ? bounds.Left + (bounds.Width - width) / 2 : bounds.Left;
        var y = request.Center ? bounds.Top + (bounds.Height - height) / 2 : bounds.Top;
        return new Rectangle(x, y, width, height);
    }

    private static PrinterCapability ReadCapability(string name)
    {
        var settings = new PrinterSettings { PrinterName = name };
        var papers = settings.PaperSizes.Cast<PaperSize>().Select(p => new PaperCapability(p.PaperName, (int)p.RawKind, p.Width, p.Height)).ToArray();
        var sources = settings.PaperSources.Cast<PaperSource>().Select(p => new SourceCapability(p.SourceName, (int)p.RawKind)).ToArray();
        var resolutions = settings.PrinterResolutions.Cast<PrinterResolution>().Select(p => new ResolutionCapability(
            string.IsNullOrWhiteSpace(p.ToString()) ? $"{p.X} × {p.Y} dpi" : p.ToString(), p.X, p.Y, (int)p.Kind)).ToArray();
        var status = settings.IsValid ? NativeMethods.GetStatus(name) : "unavailable";
        return new PrinterCapability(PrinterIdentity.FromName(name), name, settings.IsDefaultPrinter, settings.IsValid, status,
            settings.SupportsColor, settings.CanDuplex,
            Math.Max(1, settings.MaximumCopies), NativeMethods.SupportsCollate(name), papers, sources, resolutions);
    }

    public static string ResolutionKey(ResolutionCapability value) => $"{value.RawKind}:{value.X}:{value.Y}";
    private static string ResolutionKey(PrinterResolution value) => $"{(int)value.Kind}:{value.X}:{value.Y}";
    private static string SafeName(string name)
    {
        var cleaned = string.Concat(Path.GetFileNameWithoutExtension(name).Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)));
        return cleaned.Length == 0 ? "Document" : cleaned[..Math.Min(cleaned.Length, 60)];
    }

    private static class NativeMethods
    {
        private const short DcCollate = 22;
        private const uint PrinterStatusPaused = 0x00000001;
        private const uint PrinterStatusError = 0x00000002;
        private const uint PrinterStatusPaperJam = 0x00000008;
        private const uint PrinterStatusPaperOut = 0x00000010;
        private const uint PrinterStatusManualFeed = 0x00000020;
        private const uint PrinterStatusPaperProblem = 0x00000040;
        private const uint PrinterStatusOffline = 0x00000080;
        private const uint PrinterStatusNotAvailable = 0x00001000;
        private const uint PrinterStatusUserIntervention = 0x00100000;
        private const uint PrinterStatusDoorOpen = 0x00400000;
        private const uint PrinterStatusServerUnknown = 0x00800000;
        private const uint UnavailableMask = PrinterStatusError | PrinterStatusPaperJam | PrinterStatusPaperOut |
            PrinterStatusManualFeed | PrinterStatusPaperProblem | PrinterStatusUserIntervention | PrinterStatusDoorOpen;
        private const uint OfflineMask = PrinterStatusPaused | PrinterStatusOffline | PrinterStatusNotAvailable |
            PrinterStatusServerUnknown;

        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int DeviceCapabilities(string device, string? port, short capability, IntPtr output, IntPtr devMode);
        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool OpenPrinter(string printerName, out IntPtr printer, IntPtr defaults);
        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool GetPrinter(IntPtr printer, uint level, IntPtr buffer, uint size, out uint needed);
        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr printer);

        internal static bool SupportsCollate(string printerName) => DeviceCapabilities(printerName, null, DcCollate, IntPtr.Zero, IntPtr.Zero) > 0;

        internal static string GetStatus(string printerName)
        {
            if (!OpenPrinter(printerName, out var printer, IntPtr.Zero)) return "unknown";
            try
            {
                _ = GetPrinter(printer, 2, IntPtr.Zero, 0, out var needed);
                if (needed == 0) return "unknown";
                var buffer = Marshal.AllocHGlobal(checked((int)needed));
                try
                {
                    if (!GetPrinter(printer, 2, buffer, needed, out _)) return "unknown";
                    var status = Marshal.PtrToStructure<PrinterInfo2>(buffer).Status;
                    if ((status & OfflineMask) != 0) return "offline";
                    return (status & UnavailableMask) != 0 ? "unavailable" : "ready";
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            catch { return "unknown"; }
            finally { ClosePrinter(printer); }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PrinterInfo2
        {
            public IntPtr ServerName;
            public IntPtr PrinterName;
            public IntPtr ShareName;
            public IntPtr PortName;
            public IntPtr DriverName;
            public IntPtr Comment;
            public IntPtr Location;
            public IntPtr DevMode;
            public IntPtr SeparatorFile;
            public IntPtr PrintProcessor;
            public IntPtr DataType;
            public IntPtr Parameters;
            public IntPtr SecurityDescriptor;
            public uint Attributes;
            public uint Priority;
            public uint DefaultPriority;
            public uint StartTime;
            public uint UntilTime;
            public uint Status;
            public uint JobCount;
            public uint AveragePagesPerMinute;
        }
    }
}

public sealed class PrintValidationException(string message) : Exception(message);
