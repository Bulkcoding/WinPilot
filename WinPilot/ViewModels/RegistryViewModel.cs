using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinPilot.Services;

namespace WinPilot.ViewModels;

public class RegistryShortcut
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Description { get; set; } = "";
}

public partial class RegistryViewModel : ObservableObject
{
    public List<RegistryShortcut> PinnedShortcuts { get; } = RegistryService.GetPinnedShortcuts()
        .Select(s => new RegistryShortcut { Name = s.Name, Path = s.Path, Description = s.Description })
        .ToList();

    public List<RegistryShortcut> CommonShortcuts { get; } = RegistryService.GetDefaultShortcuts()
        .Select(s => new RegistryShortcut { Name = s.Name, Path = s.Path, Description = s.Description })
        .ToList();

    [RelayCommand]
    private void OpenInRegedit(RegistryShortcut? shortcut)
    {
        if (shortcut == null) return;
        RegistryService.OpenInRegedit(shortcut.Path);
    }
}
