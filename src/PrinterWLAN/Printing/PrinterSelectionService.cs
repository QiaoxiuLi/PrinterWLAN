using PrinterWLAN.Models;
using PrinterWLAN.Storage;

namespace PrinterWLAN.Printing;

public sealed class PrinterSelectionService(AppDatabase database, IPrinterService printers)
{
    public const string SelectedPrinterIdKey = "selected_printer_id";
    public const string SelectedPrinterNameKey = "selected_printer_name";

    public async Task<PrinterSelectionState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var selectedId = await database.GetSettingAsync(SelectedPrinterIdKey, string.Empty, cancellationToken);
        var selectedName = await database.GetSettingAsync(SelectedPrinterNameKey, string.Empty, cancellationToken);
        var available = await printers.GetPrintersAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(selectedId))
            return new(null, null, "unconfigured", null, available);

        var selected = available.FirstOrDefault(item =>
            item.Id.Equals(selectedId, StringComparison.Ordinal) &&
            item.Name.Equals(selectedName, StringComparison.Ordinal));
        return selected is null
            ? new(selectedId, selectedName, "unavailable", null, available)
            : new(selectedId, selectedName, selected.Status, selected, available);
    }

    public async Task<PrinterCapability> SelectAsync(string printerId, CancellationToken cancellationToken = default)
    {
        var printer = (await printers.GetPrintersAsync(cancellationToken))
            .FirstOrDefault(item => item.Id.Equals(printerId, StringComparison.Ordinal));
        if (printer is null || !printer.IsValid || printer.Status is "offline" or "unavailable")
            throw new PrintValidationException("这台打印机当前不可用，无法设为当前打印机。");
        if (printer.PaperSizes.Count == 0)
            throw new PrintValidationException("无法读取这台打印机的纸张设置，暂时不能使用。");

        await database.SetSettingsAsync(new Dictionary<string, string>
        {
            [SelectedPrinterIdKey] = printer.Id,
            [SelectedPrinterNameKey] = printer.Name
        }, cancellationToken);
        return printer;
    }

    public async Task<PrinterCapability> GetSelectedAsync(CancellationToken cancellationToken = default)
    {
        var state = await GetStateAsync(cancellationToken);
        if (state.Status == "unconfigured")
            throw new PrintValidationException("当前暂未配置打印机，请联系管理员。");
        if (state.SelectedPrinter is null || !state.SelectedPrinter.IsValid ||
            state.SelectedPrinter.Status is "offline" or "unavailable")
            throw new PrintValidationException("管理员设置的打印机当前不可用，请联系管理员。");
        return state.SelectedPrinter;
    }

    public async Task<PrintRequest> CreateRequestAsync(PrintSubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        var printer = await GetSelectedAsync(cancellationToken);
        return new PrintRequest
        {
            DocumentId = request.DocumentId,
            PrinterId = printer.Id,
            PrinterName = printer.Name,
            PaperSize = request.PaperSize,
            Orientation = request.Orientation,
            Duplex = request.Duplex,
            PageRange = request.PageRange,
            Copies = request.Copies,
            Collate = request.Collate,
            ColorMode = request.ColorMode,
            PaperSource = request.PaperSource,
            Resolution = request.Resolution,
            ScaleMode = request.ScaleMode,
            ScalePercent = request.ScalePercent,
            Center = request.Center
        };
    }
}
