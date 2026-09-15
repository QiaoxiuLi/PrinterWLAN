using PrinterWLAN.Printing;

namespace PrinterWLAN.Tests;

public sealed class PageRangeParserTests
{
    [Fact] public void ParsesMixedRanges() => Assert.Equal([1, 2, 3, 5, 7, 8, 9], PageRangeParser.Parse("1-3,5,7-9", 10));
    [Fact] public void RemovesDuplicatesAndSorts() => Assert.Equal([1, 2, 3, 5], PageRangeParser.Parse("5,1-3,2,5", 5));
    [Theory]
    [InlineData("0")]
    [InlineData("2-1")]
    [InlineData("1,,2")]
    [InlineData("x")]
    public void RejectsInvalidRanges(string value) => Assert.Throws<FormatException>(() => PageRangeParser.Parse(value, 5));
    [Fact] public void RejectsOutOfRange() => Assert.Throws<FormatException>(() => PageRangeParser.Parse("6", 5));
    [Fact] public void AllowsExactlyTwentyRequestedPages() => PageRangeParser.ValidateJobLimit(5, 4);
    [Fact] public void RejectsMoreThanTwentyRequestedPages() => Assert.Throws<FormatException>(() => PageRangeParser.ValidateJobLimit(5, 5));
}
