using System.IO;
using System.Text.Json;

namespace WinPilot.Models;

/// <summary>
/// Stores a two-key hotkey combination.
/// Keys are stored as Win32 virtual-key codes for the global hook.
/// </summary>
public class HotkeySetting
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WinPilot", "hotkey.json");

    public bool IsEnabled { get; set; } = true;
    /// <summary>First key virtual-key code (default: VK_SPACE = 0x20)</summary>
    public int Key1 { get; set; } = 32;
    /// <summary>Second key virtual-key code (default: VK_TAB = 0x09)</summary>
    public int Key2 { get; set; } = 9;

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch { /* settings save failure is non-critical */ }
    }

    public static HotkeySetting Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<HotkeySetting>(json) ?? new HotkeySetting();
            }
        }
        catch { /* fall through to defaults */ }
        return new HotkeySetting();
    }
}
