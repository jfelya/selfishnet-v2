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

    private static string DirPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SelfishNetV2");

    private static string FilePath => Path.Combine(DirPath, "settings.json");

    public static SettingsStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<SettingsStore>(File.ReadAllText(FilePath)) ?? new SettingsStore();
        }
        catch { /* fall through to defaults */ }
        return new SettingsStore();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DirPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* non-fatal */ }
    }
}
