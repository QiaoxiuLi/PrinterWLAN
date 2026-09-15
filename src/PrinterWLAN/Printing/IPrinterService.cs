using PrinterWLAN.Models;

namespace PrinterWLAN.Printing;

public interface IPrinterService
{
    Task<IReadOnlyList<PrinterCapability>> GetPrintersAsync(CancellationToken cancellationToken = default);
    Task<PrinterCapability> ValidateAsync(PrintRequest request, CancellationToken cancellationToken = default);
    Task SubmitAsync(DocumentRecord document, PrintRequest request, string jobId, CancellationToken cancellationToken);
}
