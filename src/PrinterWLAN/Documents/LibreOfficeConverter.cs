using System.Diagnostics;
using Microsoft.Extensions.Options;
using PrinterWLAN.Models;

namespace PrinterWLAN.Documents;

public interface IWordConverter
{
    Task<string> ConvertToPdfAsync(string inputPath, string workingDirectory, CancellationToken cancellationToken);
}

public sealed class LibreOfficeConverter(IOptions<PrinterWlanOptions> options, ILogger<LibreOfficeConverter> logger) : IWordConverter
{
    public async Task<string> ConvertToPdfAsync(string inputPath, string workingDirectory, CancellationToken cancellationToken)
    {
        var configured = options.Value.LibreOfficePath;
        var executable = Path.IsPathRooted(configured) ? configured : Path.Combine(AppContext.BaseDirectory, configured);
        if (!File.Exists(executable)) throw new DocumentException("暂时无法转换 Word 文件。", $"LibreOffice not found: {executable}");

        var outputDirectory = Path.Combine(workingDirectory, "converted");
        var profileDirectory = Path.Combine(workingDirectory, "lo-profile");
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(profileDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add($"-env:UserInstallation={new Uri(profileDirectory + Path.DirectorySeparatorChar).AbsoluteUri}");
        foreach (var arg in new[] { "--headless", "--nologo", "--nodefault", "--nofirststartwizard", "--norestore", "--convert-to", "pdf", "--outdir", outputDirectory, inputPath })
            startInfo.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new DocumentException("暂时无法转换 Word 文件。", "LibreOffice process failed to start.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.WordConversionTimeoutSeconds));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new DocumentException("Word 文件转换超时，请检查文件后重试。", "LibreOffice conversion timed out.");
        }
        catch
        {
            TryKill(process);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            logger.LogError("LibreOffice conversion exited with {ExitCode}. stderr: {Stderr}", process.ExitCode, Limit(stderr));
            throw new DocumentException("无法打开这个 Word 文件，请确认文件没有损坏。", $"LibreOffice exit {process.ExitCode}: {Limit(stderr)}");
        }
        var expected = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(inputPath) + ".pdf");
        if (!File.Exists(expected) || new FileInfo(expected).Length == 0)
            throw new DocumentException("无法打开这个 Word 文件，请确认文件没有损坏。", $"LibreOffice output missing. stdout: {Limit(stdout)}; stderr: {Limit(stderr)}");
        return expected;
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
    }
    private static string Limit(string value) => value.Length <= 2000 ? value : value[..2000];
}

public sealed class DocumentException(string friendlyMessage, string internalMessage, Exception? inner = null)
    : Exception(internalMessage, inner)
{
    public string FriendlyMessage { get; } = friendlyMessage;
}
