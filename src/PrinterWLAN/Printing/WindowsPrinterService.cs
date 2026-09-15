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
        var printer = (await GetPrintersAsync(cancellationToken)).FirstOrDefault(p => p.Name.Equals(request.PrinterName, StringComparison.Ordinal));
        if (printer is null || !printer.IsValid) throw new PrintValidationException("所选打印机当前不可用，请重新选择。");
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
            using var bitmap = Conversion.ToImage(document.PdfPath, request.SelectedPages[pageIndex] - 1);
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = encoded.AsStream();
            using var image = Image.FromStream(stream);
            var pdfSize = Conversion.GetPageSize(document.PdfPath, request.SelectedPages[pageIndex] - 1);
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
        return new PrinterCapability(name, settings.IsDefaultPrinter, settings.IsValid, settings.SupportsColor, settings.CanDuplex,
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
        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int DeviceCapabilities(string device, string? port, short capability, IntPtr output, IntPtr devMode);
        internal static bool SupportsCollate(string printerName) => DeviceCapabilities(printerName, null, DcCollate, IntPtr.Zero, IntPtr.Zero) > 0;
    }
}

public sealed class PrintValidationException(string message) : Exception(message);
