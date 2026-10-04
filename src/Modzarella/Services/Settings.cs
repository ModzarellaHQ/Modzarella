using System.Text.Json;

namespace Modzarella;

public class Settings
{
    public const string DefaultSource = "https://modza.space/modz/";
    public string Source { get; set; } = DefaultSource;
    public string? GameDir { get; set; }

    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Modzarella", "settings.json");

    public static Settings Load()
    {
        var s = File.Exists(FilePath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new() : new();
        if (s.Source is "https://modzarellahq.github.io/Modz/" or "https://modza.space/Modz/") s.Source = DefaultSource;
        return s;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
