namespace PrinterWLAN;

public static class ProductInfo
{
    public static string Version { get; } =
        typeof(ProductInfo).Assembly.GetName().Version?.ToString(3) ?? "unknown";
}
