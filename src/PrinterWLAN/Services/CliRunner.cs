using System.Diagnostics;
using System.Drawing.Printing;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Extensions.Configuration;
using PrinterWLAN.Authentication;
using PrinterWLAN.Storage;

namespace PrinterWLAN.Services;

public static class CliRunner
{
    public static async Task<int?> TryRunAsync(string[] args)
    {
        if (args.Length == 0) return null;
        if (args[0].Equals("pwd", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("用法：printerwlan pwd \"你的密码\"");
                return 2;
            }
            try
            {
                var paths = new AppPaths(); paths.EnsureCreated();
                var database = new AppDatabase(paths); await database.InitializeAsync();
                var admin = new AdminService(database, new PasswordService());
                await admin.SetPasswordAsync(args[1]);
                Console.WriteLine("管理员密码已更新，立即生效。");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"密码设置失败：{exception.Message}");
                return 1;
            }
        }
        if (args[0].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            var configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: true).Build();
            var port = configuration.GetValue("PrinterWLAN:Port", 8080);
            var serviceState = await QueryServiceAsync();
            var healthy = false;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                healthy = (await client.GetAsync($"http://127.0.0.1:{port}/health")).IsSuccessStatusCode;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            Console.WriteLine($"PrinterWLAN v{ProductInfo.Version}");
            Console.WriteLine($"服务状态：{serviceState}");
            Console.WriteLine($"网站状态：{(healthy ? "正常" : "无法访问，请查看 Diagnostics 日志确认端口是否被占用")}");
            Console.WriteLine($"HTTP 端口：{port}");
            foreach (var address in LocalAddresses()) Console.WriteLine($"网站地址：http://{address}:{port}");
            var count = OperatingSystem.IsWindows() ? PrinterSettings.InstalledPrinters.Count : 0;
            Console.WriteLine($"可用打印机：{count}");
            return healthy ? 0 : 1;
        }
        if (args[0].Equals("doctor", StringComparison.OrdinalIgnoreCase))
            return await RunDoctorAsync();
        if (args[0] is "--help" or "-h" or "help")
        {
            Console.WriteLine("PrinterWLAN 管理命令\n  printerwlan status\n  printerwlan doctor\n  printerwlan pwd \"你的密码\"");
            return 0;
        }
        // ASP.NET Core and Windows Service hosting pass configuration switches here.
        // Only the explicit management verbs above belong to this CLI dispatcher.
        if (args[0].StartsWith('-')) return null;
        Console.Error.WriteLine("未知命令。可用命令：status、doctor、pwd");
        return 2;
    }

    private static async Task<int> RunDoctorAsync()
    {
        Console.WriteLine($"PrinterWLAN v{ProductInfo.Version} 兼容性检查");
        var failures = 0;
        var warnings = 0;

        void Report(bool success, string successText, string failureText, bool warning = false)
        {
            if (success) { Console.WriteLine($"[通过] {successText}"); return; }
            Console.WriteLine($"[{(warning ? "提示" : "失败")}] {failureText}");
            if (warning) warnings++; else failures++;
        }

        var compatibility = WindowsCompatibility.Current;
        Report(compatibility.IsSupported, compatibility.PlatformName, compatibility.Message);
        Console.WriteLine($"       系统：{RuntimeInformation.OSDescription.Trim()}；系统架构：{RuntimeInformation.OSArchitecture}；进程架构：{RuntimeInformation.ProcessArchitecture}");

        var baseDirectory = AppContext.BaseDirectory;
        foreach (var relativePath in new[]
        {
            "appsettings.json",
            "wwwroot/vendor/pdfjs/pdf.mjs",
            "wwwroot/vendor/pdfjs/pdf.worker.mjs",
            "third-party/LibreOffice/program/soffice.exe",
            "e_sqlite3.dll",
            "libSkiaSharp.dll",
            "pdfium.dll"
        })
        {
            var exists = File.Exists(Path.Combine(baseDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            Report(exists, $"已包含 {relativePath}", $"安装内容缺少 {relativePath}");
        }

        foreach (var library in new[] { "e_sqlite3.dll", "libSkiaSharp.dll", "pdfium.dll" })
        {
            var loaded = false;
            IntPtr handle = IntPtr.Zero;
            try { loaded = NativeLibrary.TryLoad(Path.Combine(baseDirectory, library), out handle); }
            catch (Exception) { }
            finally { if (handle != IntPtr.Zero) NativeLibrary.Free(handle); }
            Report(loaded, $"原生组件可加载：{library}", $"原生组件无法加载：{library}（请重新运行安装程序）");
        }

        var paths = new AppPaths();
        try
        {
            paths.EnsureCreated();
            var probe = Path.Combine(paths.Temp, $"doctor-{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(probe, "PrinterWLAN");
            File.Delete(probe);
            Report(true, "程序数据目录可读写", string.Empty);
        }
        catch (Exception exception)
        {
            Report(false, string.Empty, $"程序数据目录不可写：{exception.Message}");
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var spooler = new ServiceController("Spooler");
                Report(spooler.Status == ServiceControllerStatus.Running, "Windows 打印后台处理服务正在运行", "Windows 打印后台处理服务未运行");
            }
            catch (Exception exception)
            {
                Report(false, string.Empty, $"无法检查 Windows 打印后台处理服务：{exception.Message}");
            }

            try
            {
                var count = PrinterSettings.InstalledPrinters.Count;
                Report(count > 0, $"Windows 可见打印机：{count} 台", "尚未找到打印机；请先在 Windows 中安装打印机", warning: true);
            }
            catch (Exception exception)
            {
                Report(false, string.Empty, $"无法读取 Windows 打印机：{exception.Message}");
            }
        }

        Console.WriteLine(failures == 0
            ? $"检查完成：没有发现阻止运行的问题{(warnings > 0 ? $"，另有 {warnings} 项提示" : string.Empty)}。"
            : $"检查完成：发现 {failures} 项必须修复的问题。请重新运行安装程序或联系管理员。");
        return failures == 0 ? 0 : 1;
    }

    private static async Task<string> QueryServiceAsync()
    {
        if (!OperatingSystem.IsWindows()) return "非 Windows 开发环境";
        try
        {
            var start = new ProcessStartInfo("sc.exe") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            start.ArgumentList.Add("query"); start.ArgumentList.Add("PrinterWLAN");
            using var process = Process.Start(start);
            if (process is null) return "未知";
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) ? "正在运行" : "未运行";
        }
        catch (Exception) { return "未知"; }
    }

    private static IEnumerable<string> LocalAddresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
        .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
        .Select(address => address.Address.ToString()).Distinct(StringComparer.Ordinal);
}
