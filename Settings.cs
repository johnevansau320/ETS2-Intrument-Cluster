using System.IO;
using System.Text.Json;

namespace Ets2Cluster;

/// <summary>Remembered window position and options (%APPDATA%\ETS2-Cluster\cluster.json).</summary>
public sealed class Settings
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 960;
    public double Height { get; set; } = 540;
    public double Opacity { get; set; } = 1.0;
    public bool Topmost { get; set; } = true;
    public bool Mph { get; set; } = false;
    /// <summary>Show the truck make (VOLVO ...) under the speed.</summary>
    public bool ShowMake { get; set; } = true;
    public string Host { get; set; } = "127.0.0.1:31377";
    /// <summary>Look for a newer release on GitHub now and then (about once a day). Nothing is downloaded or installed by it.</summary>
    public bool CheckUpdates { get; set; } = true;
    public long LastUpdateCheckUtc { get; set; }
    /// <summary>A version the user chose to skip: no notice for it.</summary>
    public string SkippedVersion { get; set; } = "";
    /// <summary>Odometer reading when the trip counter was last reset.</summary>
    public double TripBaseKm { get; set; } = double.NaN;

    public static string? PathOverride;   // --config <file> (used for testing)
    public static string FilePath => PathOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ETS2-Cluster", "cluster.json");

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings(); }
        catch { return new Settings(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* not fatal */ }
    }
}
