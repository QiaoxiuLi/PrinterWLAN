using System.Security.Cryptography;
using System.Text;

namespace PrinterWLAN.Printing;

public static class PrinterIdentity
{
    public static string FromName(string name)
    {
        var normalized = name.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant()[..24];
    }
}
