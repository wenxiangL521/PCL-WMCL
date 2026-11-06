using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WMCL.App.ViewModels;

public partial class ModpackViewModel
{
    private bool _hasBrowsedPacks;

    public async Task EnsureResourceSearchAsync()
    {
        if (_hasBrowsedPacks || HasResourceDetail) return;
        while (IsBusy)
        {
            await Task.Delay(100);
            if (_hasBrowsedPacks || HasResourceDetail) return;
        }
        await SearchCommand.ExecuteAsync(null);
    }

    public bool HasResourceDetail => SelectedProject is not null;

    [RelayCommand]
    private void ReturnToResults() => SelectedProject = null;

    [RelayCommand]
    private void ResetSearch()
    {
        SearchText = "";
        SearchGameVersion = "";
    }
}
