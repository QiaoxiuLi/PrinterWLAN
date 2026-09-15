using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;

namespace PrinterWLAN.Authentication;

public sealed class PasswordService
{
    private readonly PasswordHasher<object> _hasher = new();
    private static readonly char[] Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789".ToCharArray();

    public string Hash(string password) => _hasher.HashPassword(this, password);

    public bool Verify(string hash, string password) =>
        _hasher.VerifyHashedPassword(this, hash, password) != PasswordVerificationResult.Failed;

    public string GenerateUserPassword()
    {
        Span<char> output = stackalloc char[10];
        for (var i = 0; i < output.Length; i++) output[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(output);
    }
}
