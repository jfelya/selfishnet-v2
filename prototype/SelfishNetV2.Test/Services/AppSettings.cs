// FREE MODEL TEST PROTOTYPE — simple persisted settings (language, last adapter).

using System.Text.Json;

namespace SelfishNetV2.Test.Services;

public sealed class AppSettings
{
    public string Language { get; set; } = "SpanishLA";
    public string? LastAdapterId { get; set; }
    public string UnitMode { get; set; } = "Bytes";

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SelfishNetV2.FreeModelTest", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* fall through to defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* non-fatal */ }
    }
}
