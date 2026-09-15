using System.Diagnostics;
using System.Drawing.Printing;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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
            Console.WriteLine("PrinterWLAN v1.0.0");
            Console.WriteLine($"服务状态：{serviceState}");
            Console.WriteLine($"网站状态：{(healthy ? "正常" : "无法访问，请查看 Diagnostics 日志确认端口是否被占用")}");
            Console.WriteLine($"HTTP 端口：{port}");
            foreach (var address in LocalAddresses()) Console.WriteLine($"网站地址：http://{address}:{port}");
            var count = OperatingSystem.IsWindows() ? PrinterSettings.InstalledPrinters.Count : 0;
            Console.WriteLine($"可用打印机：{count}");
            return healthy ? 0 : 1;
        }
        if (args[0] is "--help" or "-h" or "help")
        {
            Console.WriteLine("PrinterWLAN 管理命令\n  printerwlan status\n  printerwlan pwd \"你的密码\"");
            return 0;
        }
        // ASP.NET Core and Windows Service hosting pass configuration switches here.
        // Only the explicit management verbs above belong to this CLI dispatcher.
        if (args[0].StartsWith('-')) return null;
        Console.Error.WriteLine("未知命令。可用命令：status、pwd");
        return 2;
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
