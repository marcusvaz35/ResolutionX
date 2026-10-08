using System.Text.Json;
using ResolutionX.Core.Interfaces;
using ResolutionX.Core.Models;

namespace ResolutionX.Core.Services;

/// <summary>
/// Presets embutidos + presets do usuário gravados em JSON
/// (por padrão em %AppData%\ResolutionX\presets.json).
/// </summary>
public sealed class PresetStore : IPresetStore
{
    private static readonly ResolutionPreset[] BuiltIn =
    [
        new("720p", 1280, 720, 60, IsBuiltIn: true),
        new("1080p", 1920, 1080, 60, IsBuiltIn: true),
        new("1080p 120 Hz", 1920, 1080, 120, IsBuiltIn: true),
        new("1440p", 2560, 1440, 60, IsBuiltIn: true),
        new("Ultrawide", 3440, 1440, 60, IsBuiltIn: true),
        new("4K", 3840, 2160, 60, IsBuiltIn: true)
    ];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly List<ResolutionPreset> _userPresets;

    public PresetStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ResolutionX",
            "presets.json");
        _userPresets = Load();
    }

    public IReadOnlyList<ResolutionPreset> GetAll() => [.. BuiltIn, .. _userPresets];

    public bool Add(ResolutionPreset preset)
    {
        if (GetAll().Any(p => p.ToMode() == preset.ToMode()))
            return false;

        _userPresets.Add(preset with { IsBuiltIn = false });
        Save();
        return true;
    }

    public bool Remove(ResolutionPreset preset)
    {
        if (preset.IsBuiltIn)
            return false;

        var removed = _userPresets.RemoveAll(p => p.ToMode() == preset.ToMode()) > 0;
        if (removed)
            Save();
        return removed;
    }

    private List<ResolutionPreset> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return [];

            var loaded = JsonSerializer.Deserialize<List<ResolutionPreset>>(File.ReadAllText(_filePath));
            return loaded?
                .Where(p => p.Width > 0 && p.Height > 0 && p.RefreshRate > 0)
                .Select(p => p with { IsBuiltIn = false })
                .ToList() ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Arquivo ilegível ou corrompido: começa vazio em vez de impedir o app de abrir.
            return [];
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(_userPresets, JsonOptions));
    }
}
