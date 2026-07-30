using System;
using System.IO;
using System.Text.Json;

namespace KeySpammer;

/// <summary>
/// Last-used KPS and key, persisted to %AppData%\KeySpammer\settings.json.
/// Load() never throws -- a missing/corrupt file just falls back to
/// defaults, since a broken settings file should never stop the app from
/// starting.
/// </summary>
internal sealed class Settings
{
    public int Kps { get; set; } = 10;
    public string KeyName { get; set; } = "F6";

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KeySpammer", "settings.json");

    public static Settings Load()
    {
        try
        {
            string json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<Settings>(json);
            if (loaded is not null && loaded.Kps > 0 && !string.IsNullOrWhiteSpace(loaded.KeyName))
                return loaded;
        }
        catch
        {
            // file missing, unreadable, or corrupt JSON -- use defaults
        }
        return new Settings();
    }

    /// <summary>Best-effort save -- a failure here (e.g. locked-down AppData) shouldn't crash the app.</summary>
    public void Save()
    {
        try
        {
            string dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(this);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // best-effort persistence -- not fatal if it fails
        }
    }
}
