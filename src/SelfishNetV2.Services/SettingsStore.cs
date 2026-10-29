using System.Text.Json;

namespace SelfishNetV2.Services;

/// <summary>Persisted app settings in %AppData%\SelfishNetV2\settings.json.</summary>
public sealed class SettingsStore
{
    public string Language { get; set; } = "SpanishLA";
    public string? LastAdapterId { get; set; }
    public string UnitMode { get; set; } = "Bytes";
    public string Theme { get; set; } = "Dark";
    public string Engine { get; set; } = "Modern";

    private string? _filePath;

    private static string DefaultDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SelfishNetV2");

    private static string DefaultFilePath => Path.Combine(DefaultDir, "settings.json");

    public static SettingsStore Load(string? filePath = null)
    {
        string path = filePath ?? DefaultFilePath;
        var store = TryRead(path) ?? TryRead(path + ".bak") ?? new SettingsStore();
        store._filePath = path;
        return store;
    }

    private static SettingsStore? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<SettingsStore>(File.ReadAllText(path));
        }
        catch { return null; }
    }

    public void Save()
    {
        try
        {
            AtomicFile.WriteAllText(_filePath ?? DefaultFilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* non-fatal */ }
    }
}
