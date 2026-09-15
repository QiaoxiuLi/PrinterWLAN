using System.Text;
using PrinterWLAN.Authentication;
using PrinterWLAN.Users;

namespace PrinterWLAN.Tests;

public sealed class UserAndCsvTests
{
    [Fact]
    public void ParsesChineseBomAndOptionalHeader()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("用户名\r\n张三\r\n\"John Smith\"\r\n")).ToArray());
        var result = UserService.ParseCsv(stream, out var ignored);
        Assert.Equal(["张三", "John Smith"], result);
        Assert.Equal(0, ignored);
    }

    [Fact]
    public void FirstRowIsUserWithoutHeader()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Alice\nBob"));
        Assert.Equal(["Alice", "Bob"], UserService.ParseCsv(stream, out _));
    }

    [Fact]
    public void EmptyRowsAreCounted()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Alice\n\nBob\n"));
        _ = UserService.ParseCsv(stream, out var ignored);
        Assert.True(ignored >= 1);
    }

    [Fact] public void UsernameNormalizationIsUnicodeAndCaseInsensitive() =>
        Assert.Equal(UserService.NormalizeUsername(" élan "), UserService.NormalizeUsername("E\u0301LAN"));

    [Fact]
    public void PasswordsAreExactlyTenRandomCharacters()
    {
        var service = new PasswordService();
        var values = Enumerable.Range(0, 100).Select(_ => service.GenerateUserPassword()).ToArray();
        Assert.All(values, value => Assert.Matches("^[A-Za-z0-9]{10}$", value));
        Assert.Equal(values.Length, values.Distinct().Count());
    }

    [Fact]
    public void PasswordHashVerifiesAndDoesNotContainPlaintext()
    {
        var service = new PasswordService(); var password = Guid.NewGuid().ToString("N"); var hash = service.Hash(password);
        Assert.True(service.Verify(hash, password)); Assert.DoesNotContain(password, hash, StringComparison.Ordinal);
    }
}
