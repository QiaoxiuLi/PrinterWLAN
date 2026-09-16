using System.Runtime.InteropServices;
using PrinterWLAN.Services;

namespace PrinterWLAN.Tests;

public sealed class WindowsCompatibilityTests
{
    [Theory]
    [InlineData(17763)]
    [InlineData(19045)]
    [InlineData(22000)]
    [InlineData(26100)]
    public void SupportedX64WindowsBuildsAreAccepted(int build)
    {
        var result = WindowsCompatibility.Evaluate(true, new Version(10, 0, build), Architecture.X64);
        Assert.True(result.IsSupported);
    }

    [Fact]
    public void Windows11Arm64IsAcceptedForX64CompatibilityMode()
    {
        var result = WindowsCompatibility.Evaluate(true, new Version(10, 0, 26100), Architecture.Arm64);
        Assert.True(result.IsSupported);
    }

    [Theory]
    [InlineData(false, 26100, Architecture.X64)]
    [InlineData(true, 17134, Architecture.X64)]
    [InlineData(true, 19045, Architecture.X86)]
    [InlineData(true, 19045, Architecture.Arm64)]
    public void UnsupportedPlatformsAreRejected(bool isWindows, int build, Architecture architecture)
    {
        var result = WindowsCompatibility.Evaluate(isWindows, new Version(10, 0, build), architecture);
        Assert.False(result.IsSupported);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }
}
