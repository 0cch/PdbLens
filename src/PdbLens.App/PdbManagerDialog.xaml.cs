using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PdbLens.Core.Models;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PdbLens.App;

public sealed partial class PdbManagerDialog : ContentDialog
{
    private readonly Window _owner;
    private readonly ObservableCollection<PdbManagerItem> _items = new();
    private readonly HashSet<uint> _removedPdbIds = new();
    private readonly HashSet<string> _knownPaths = new(StringComparer.OrdinalIgnoreCase);

    public PdbManagerDialog(Window owner, IEnumerable<PdbFileInfo> pdbs)
    {
        _owner = owner;
        InitializeComponent();

        foreach (var pdb in pdbs)
        {
            _knownPaths.Add(pdb.Path);
            _items.Add(new PdbManagerItem(pdb.PdbId, pdb.Name, pdb.Path, pdb.SymbolCount));
        }

        PdbList.ItemsSource = _items;
        UpdateState();
    }

    public IReadOnlyCollection<uint> RemovedPdbIds => _removedPdbIds;

    public IReadOnlyList<string> AddedPaths =>
        _items.Where(item => item.IsPending).Select(item => item.Path).ToArray();

    private async void AddPdb_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".pdb");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_owner));

        var files = await picker.PickMultipleFilesAsync();
        foreach (var file in files)
        {
            if (!_knownPaths.Add(file.Path))
            {
                continue;
            }

            _items.Add(new PdbManagerItem(0, file.Name, file.Path, null));
        }

        UpdateState();
    }

    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = PdbList.SelectedItems.Cast<PdbManagerItem>().ToArray();
        foreach (var item in selected)
        {
            if (!item.IsPending)
            {
                _removedPdbIds.Add(item.PdbId);
            }

            _knownPaths.Remove(item.Path);
            _items.Remove(item);
        }

        PdbList.SelectedItems.Clear();
        UpdateState();
    }

    private void PdbList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RemoveSelectedButton.IsEnabled = PdbList.SelectedItems.Count > 0;

    private void UpdateState()
    {
        var loadedCount = _items.Count(item => !item.IsPending);
        var pendingCount = _items.Count(item => item.IsPending);
        SummaryText.Text = $"{loadedCount:N0} loaded" +
            (pendingCount > 0 ? $" · {pendingCount:N0} to add" : string.Empty);
        EmptyState.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PdbList.Visibility = _items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RemoveSelectedButton.IsEnabled = PdbList.SelectedItems.Count > 0;
    }
}
