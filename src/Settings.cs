using System.Text.Json;

namespace Modzarella;

public class Settings
{
    public const string DefaultSource = "https://modzarellahq.github.io/Modz/";
    public string Source { get; set; } = DefaultSource;
    public string? GameDir { get; set; }

    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Modzarella", "settings.json");

    public static Settings Load() =>
        File.Exists(FilePath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new() : new();

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
