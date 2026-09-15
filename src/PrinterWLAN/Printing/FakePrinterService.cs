using PrinterWLAN.Models;

namespace PrinterWLAN.Printing;

public sealed class FakePrinterService : IPrinterService
{
    public static readonly PrinterCapability Capability = new("PrinterWLAN Test Printer", true, true, true, true, 99, true,
        [new("A4", 9, 827, 1169), new("Letter", 1, 850, 1100)], [new("Auto", 7)],
        [new("600 dpi", 600, 600, 0)]);
    public Task<IReadOnlyList<PrinterCapability>> GetPrintersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PrinterCapability>>([Capability]);
    public Task<PrinterCapability> ValidateAsync(PrintRequest request, CancellationToken cancellationToken = default)
    {
        if (request.PrinterName != Capability.Name || !Capability.PaperSizes.Any(p => p.Name == request.PaperSize))
            throw new PrintValidationException("所选打印设置已不可用，请重新选择。");
        return Task.FromResult(Capability);
    }
    public Task SubmitAsync(DocumentRecord document, PrintRequest request, string jobId, CancellationToken cancellationToken) => Task.CompletedTask;
}
