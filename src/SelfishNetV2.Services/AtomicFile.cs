namespace SelfishNetV2.Services;

/// <summary>
/// Replace a JSON file without leaving a half-written live file behind. The new
/// contents go to a sibling temp file, then replace the old file; the replaced
/// version is kept as .bak. A crash leaves either the old file or the complete new
/// one, never a truncated one.
/// </summary>
internal static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        string temp = path + ".tmp";
        File.WriteAllText(temp, contents);

        if (!File.Exists(path))
        {
            File.Move(temp, path);
            return;
        }

        string backup = path + ".bak";
        try
        {
            File.Replace(temp, path, backup, ignoreMetadataErrors: true);
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException)
        {
            File.Move(temp, path, overwrite: true);
            try { File.Copy(path, backup, overwrite: true); } catch { }
        }
    }
}
