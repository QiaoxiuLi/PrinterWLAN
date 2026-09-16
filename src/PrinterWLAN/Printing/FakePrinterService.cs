using PrinterWLAN.Models;

namespace PrinterWLAN.Printing;

public sealed class FakePrinterService : IPrinterService
{
    public static readonly PrinterCapability Capability = new(PrinterIdentity.FromName("PrinterWLAN Test Printer A"),
        "PrinterWLAN Test Printer A", true, true, "ready", true, true, 99, true,
        [new("A4", 9, 827, 1169), new("Letter", 1, 850, 1100)], [new("Auto", 7)],
        [new("600 dpi", 600, 600, 0)]);
    public static readonly PrinterCapability SecondaryCapability = new(PrinterIdentity.FromName("PrinterWLAN Test Printer B"),
        "PrinterWLAN Test Printer B", false, true, "ready", false, false, 20, false,
        [new("A4", 9, 827, 1169)], [new("Auto", 7)], [new("300 dpi", 300, 300, 0)]);
    public Task<IReadOnlyList<PrinterCapability>> GetPrintersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PrinterCapability>>([Capability, SecondaryCapability]);
    public Task<PrinterCapability> ValidateAsync(PrintRequest request, CancellationToken cancellationToken = default)
    {
        var capability = new[] { Capability, SecondaryCapability }.FirstOrDefault(item =>
            item.Id == request.PrinterId && item.Name == request.PrinterName);
        if (capability is null || !capability.PaperSizes.Any(p => p.Name == request.PaperSize))
            throw new PrintValidationException("所选打印设置已不可用，请重新选择。");
        if (request.ColorMode == "color" && !capability.SupportsColor)
            throw new PrintValidationException("这台打印机不支持彩色打印。");
        if (request.Duplex != "simplex" && !capability.CanDuplex)
            throw new PrintValidationException("这台打印机不支持双面打印。");
        return Task.FromResult(capability);
    }
    public Task SubmitAsync(DocumentRecord document, PrintRequest request, string jobId, CancellationToken cancellationToken) => Task.CompletedTask;
}
