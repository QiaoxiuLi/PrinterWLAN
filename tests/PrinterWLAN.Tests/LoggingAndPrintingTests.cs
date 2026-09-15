using System.Drawing;
using PrinterWLAN.Logging;
using PrinterWLAN.Models;
using PrinterWLAN.Printing;

namespace PrinterWLAN.Tests;

public sealed class LoggingAndPrintingTests
{
    [Theory]
    [InlineData("2026-09-15", "2026-09-15", "2026-09-24")]
    [InlineData("2026-09-24", "2026-09-15", "2026-09-24")]
    [InlineData("2026-09-25", "2026-09-25", "2026-10-04")]
    [InlineData("2027-01-01", "2026-12-24", "2027-01-02")]
    public void TenDaySlicesCrossMonthsAndYears(string value, string expectedStart, string expectedEnd)
    {
        var slice = ActivityLogService.SliceFor(DateOnly.Parse("2026-09-15"), DateOnly.Parse(value));
        Assert.Equal(DateOnly.Parse(expectedStart), slice.Start); Assert.Equal(DateOnly.Parse(expectedEnd), slice.End);
    }

    [Fact]
    public void FitScaleCentersInsideBounds()
    {
        var request = new PrintRequest { DocumentId = "d", PrinterName = "p", PaperSize = "A4", ScaleMode = "fit", Center = true };
        var target = WindowsPrinterService.CalculateTarget(new Rectangle(10, 20, 800, 1000), 600, 1200, request);
        Assert.True(target.Left >= 10 && target.Top >= 20 && target.Right <= 810 && target.Bottom <= 1020);
    }

    [Fact]
    public async Task FakePrinterRejectsUnknownCapability()
    {
        var fake = new FakePrinterService();
        var request = new PrintRequest { DocumentId = "d", PrinterName = "missing", PaperSize = "A4" };
        await Assert.ThrowsAsync<PrintValidationException>(() => fake.ValidateAsync(request));
    }
}
