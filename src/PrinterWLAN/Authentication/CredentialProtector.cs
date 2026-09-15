using System.Security.Cryptography;
using System.Text;
namespace PrinterWLAN.Authentication;

public sealed class CredentialProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PrinterWLAN-user-export-v1");

    public byte[] Protect(string value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI credential storage requires Windows.");
        return ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.LocalMachine);
    }

    public string Unprotect(byte[] value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI credential storage requires Windows.");
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(value, Entropy, DataProtectionScope.LocalMachine));
    }
}
