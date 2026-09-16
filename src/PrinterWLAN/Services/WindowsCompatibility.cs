using System.Runtime.InteropServices;

namespace PrinterWLAN.Services;

public sealed record WindowsCompatibilityResult(bool IsSupported, string PlatformName, string Message);

public static class WindowsCompatibility
{
    public const int MinimumWindowsBuild = 17763;
    public const int MinimumWindows11Build = 22000;

    public static WindowsCompatibilityResult Current => Evaluate(
        OperatingSystem.IsWindows(), Environment.OSVersion.Version, RuntimeInformation.OSArchitecture);

    public static WindowsCompatibilityResult Evaluate(bool isWindows, Version version, Architecture architecture)
    {
        if (!isWindows)
            return new(false, "非 Windows 系统", "PrinterWLAN 只能安装在 Windows 上。");

        if (version.Major < 10 || version.Build < MinimumWindowsBuild)
            return new(false, $"Windows {version}", "需要 Windows 10 1809 或更高版本。");

        var isWindows11 = version.Build >= MinimumWindows11Build;
        var platformName = isWindows11 ? "Windows 11 / Windows Server 2025" : "Windows 10 / Windows Server 2019-2022";
        if (architecture == Architecture.X64)
            return new(true, platformName + " x64", "系统版本和处理器架构受支持。");

        if (architecture == Architecture.Arm64 && isWindows11)
            return new(true, "Windows 11 ARM64（x64 兼容模式）", "系统支持运行 PrinterWLAN x64 安装包。");

        return new(false, platformName + $" {architecture}", "需要 x64 Windows 10，或 x64/ARM64 Windows 11。");
    }
}
