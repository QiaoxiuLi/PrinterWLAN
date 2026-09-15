namespace PrinterWLAN.Printing;

public static class PageRangeParser
{
    public static IReadOnlyList<int> Parse(string? value, int totalPages)
    {
        if (totalPages <= 0) throw new ArgumentOutOfRangeException(nameof(totalPages));
        if (string.IsNullOrWhiteSpace(value) || value.Equals("all", StringComparison.OrdinalIgnoreCase))
            return Enumerable.Range(1, totalPages).ToArray();
        var pages = new SortedSet<int>();
        foreach (var rawPart in value.Split(',', StringSplitOptions.TrimEntries))
        {
            if (rawPart.Length == 0) throw new FormatException("页码范围格式不正确。");
            var bounds = rawPart.Split('-', StringSplitOptions.TrimEntries);
            if (bounds.Length == 1)
            {
                Add(ParsePage(bounds[0]), totalPages, pages);
            }
            else if (bounds.Length == 2)
            {
                var start = ParsePage(bounds[0]);
                var end = ParsePage(bounds[1]);
                if (start > end) throw new FormatException("页码范围的起始页不能大于结束页。");
                if (end > totalPages) throw new FormatException("页码超出文件总页数。");
                for (var page = start; page <= end; page++) pages.Add(page);
            }
            else throw new FormatException("页码范围格式不正确。");
        }
        if (pages.Count == 0) throw new FormatException("请至少选择一页。");
        return pages.ToArray();
    }

    public static void ValidateJobLimit(int selectedPageCount, int copies)
    {
        if (copies < 1) throw new FormatException("份数至少为 1。");
        if (checked(selectedPageCount * copies) > 20) throw new FormatException("每个打印任务最多打印 20 页（所选页数 × 份数）。");
    }

    private static int ParsePage(string value) => int.TryParse(value, out var page) && page > 0 ? page : throw new FormatException("页码必须是正整数。");
    private static void Add(int page, int totalPages, SortedSet<int> pages)
    {
        if (page > totalPages) throw new FormatException("页码超出文件总页数。");
        pages.Add(page);
    }
}
