using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WinPilot.Services;

namespace WinPilot.ViewModels;

public partial class DefenderViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<DefenderExclusion> _exclusions = [];
    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private bool   _tamperProtectionEnabled;
    [ObservableProperty] private string _newValue = "";
    [ObservableProperty] private ExclusionType _selectedType = ExclusionType.Path;

    // 추가 버튼 활성 여부: 입력값 있고 로딩 중이 아니고 Tamper Protection OFF
    private bool CanAdd() => !string.IsNullOrWhiteSpace(NewValue)
                             && !IsLoading
                             && !TamperProtectionEnabled;

    private bool CanRemove() => !IsLoading && !TamperProtectionEnabled;

    partial void OnNewValueChanged(string value)             => AddCommand.NotifyCanExecuteChanged();
    partial void OnIsLoadingChanged(bool value)              => AddCommand.NotifyCanExecuteChanged();
    partial void OnTamperProtectionEnabledChanged(bool value)=> AddCommand.NotifyCanExecuteChanged();

    public DefenderViewModel()
    {
        _ = LoadAsync();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        TamperProtectionEnabled = await Task.Run(DefenderService.IsTamperProtectionEnabled);
        var items = await Task.Run(DefenderService.GetExclusions);
        Exclusions = new ObservableCollection<DefenderExclusion>(items);
        IsLoading = false;
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    public async Task AddAsync()
    {
        string val = NewValue.Trim();
        if (string.IsNullOrEmpty(val)) return;

        IsLoading = true;
        bool ok = await DefenderService.AddExclusionAsync(val, SelectedType);
        IsLoading = false;

        if (ok)
        {
            NewValue = "";
            await LoadAsync();
        }
        else
        {
            MessageBox.Show(
                "제외 항목 추가에 실패했습니다.\n" +
                "변조 방지(Tamper Protection)가 켜져 있거나 권한이 부족합니다.",
                "WinPilot", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    public async Task RemoveAsync(DefenderExclusion? item)
    {
        if (item == null || TamperProtectionEnabled) return;

        IsLoading = true;
        bool ok = await DefenderService.RemoveExclusionAsync(item.Value, item.Type);
        IsLoading = false;

        if (ok)
            await LoadAsync();
        else
            MessageBox.Show(
                "제외 항목 삭제에 실패했습니다.",
                "WinPilot", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    [RelayCommand]
    public void BrowseFolder()
    {
        // .NET 8+ WPF 전용 폴더 선택 다이얼로그
        var dlg = new OpenFolderDialog
        {
            Title = "제외할 폴더를 선택하세요"
        };
        if (dlg.ShowDialog() == true)
        {
            NewValue     = dlg.FolderName;
            SelectedType = ExclusionType.Path;
        }
    }
}
