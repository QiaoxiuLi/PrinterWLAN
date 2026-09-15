namespace PrinterWLAN.Storage;

public sealed class AppPaths
{
    public AppPaths()
    {
        var overridden = Environment.GetEnvironmentVariable("PRINTERWLAN_DATA_DIR");
        Root = !string.IsNullOrWhiteSpace(overridden)
            ? Path.GetFullPath(overridden)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PrinterWLAN");
        Data = Path.Combine(Root, "Data");
        Temp = Path.Combine(Root, "Temp");
        Logs = Path.Combine(Root, "Logs");
        Diagnostics = Path.Combine(Root, "Diagnostics");
        Cache = Path.Combine(Root, "Cache");
        Exports = Path.Combine(Temp, "Exports");
    }

    public string Root { get; }
    public string Data { get; }
    public string Temp { get; }
    public string Logs { get; }
    public string Diagnostics { get; }
    public string Cache { get; }
    public string Exports { get; }
    public string AppDatabase => Path.Combine(Data, "app.db");

    public void EnsureCreated()
    {
        foreach (var path in new[] { Root, Data, Temp, Logs, Diagnostics, Cache, Exports })
            Directory.CreateDirectory(path);
    }
}
