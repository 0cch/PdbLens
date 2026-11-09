using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PdbLens.Core.Models;
using PdbLens.Core.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using WinRT.Interop;

namespace PdbLens.App;

public sealed partial class MainWindow : Window
{
    private const uint PageSize = 400;
    private const int MaxRetainedSymbols = 1600;

    private readonly ObservableCollection<SymbolItem> _symbols = new();
    private readonly ObservableCollection<PdbFilterOption> _pdbFilters = new();
    private readonly List<PdbFileInfo> _pdbs = new();
    private readonly Stack<SymbolKey> _backHistory = new();
    private readonly Stack<SymbolKey> _forwardHistory = new();
    private readonly SymbolWorkspaceService? _workspaceService;
    private readonly IReadOnlyList<string> _initialPdbPaths;

    private CancellationTokenSource? _searchCancellation;
    private SymbolDefinition? _currentDefinition;
    private AboutWindow? _aboutWindow;
    private AppThemePreference _themePreference;
    private bool _initialized;
    private bool _suppressControlEvents;
    private bool _suppressSelectionLoading;
    private bool _ignoreNextSearchChange;
    private bool _ignoreNextPdbFilterChange;
    private uint _currentOffset;
    private bool _hasMore;
    private int _queryGeneration;
    private bool _resizingPane;
    private bool _manageDialogOpen;
    private double _paneStartX;
    private double _paneStartWidth;

    public MainWindow(IReadOnlyList<string>? initialPdbPaths = null)
    {
        _initialPdbPaths = initialPdbPaths ?? Array.Empty<string>();
        InitializeComponent();
        AboutButton.Click += About_Click;
        FallbackAboutButton.Click += About_Click;
        Title = "PdbLens";
        _themePreference = ThemePreferenceStore.Load();
        ApplyThemePreference(_themePreference, false);
        ResizeWindow();
        ConfigureTitleBar();
        ConfigureKeyboardAccelerators();

        RootGrid.ActualThemeChanged += RootGrid_ActualThemeChanged;
        SymbolsList.ItemsSource = _symbols;
        PdbFilterBox.ItemsSource = _pdbFilters;
        _pdbFilters.Add(new PdbFilterOption(0, "All loaded PDBs"));
        PdbFilterBox.SelectedIndex = 0;

        try
        {
            _workspaceService = new SymbolWorkspaceService();
        }
        catch (Exception error)
        {
            OpenPdbButton.IsEnabled = false;
            ShowError("Unable to initialize PdbLens", error.Message);
        }

        Closed += MainWindow_Closed;
        _ = InitializeAsync();
    }

    private void ResizeWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        var workArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Clamp(workArea.Width - 96, 900, 1440);
        var height = Math.Clamp(workArea.Height - 96, 640, 900);
        appWindow.Resize(new SizeInt32(width, height));
    }

    private void ConfigureTitleBar()
    {
        try
        {
            if (!AppWindowTitleBar.IsCustomizationSupported())
            {
                AppTitleBar.Visibility = Visibility.Collapsed;
                FallbackToolbar.Visibility = Visibility.Visible;
                return;
            }

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            TitleBarThemeHelper.Apply(this, RootGrid.ActualTheme);
            TitleBarThemeHelper.ApplyInsets(this, AppTitleBar);
            AppTitleBar.SizeChanged += (_, _) => TitleBarThemeHelper.ApplyInsets(this, AppTitleBar);
        }
        catch
        {
            AppTitleBar.Visibility = Visibility.Collapsed;
            FallbackToolbar.Visibility = Visibility.Visible;
        }
    }

    private void RootGrid_ActualThemeChanged(FrameworkElement sender, object args)
    {
        TitleBarThemeHelper.Apply(this, RootGrid.ActualTheme);
        TitleBarThemeHelper.ApplyInsets(this, AppTitleBar);
        RenderCurrentDefinition();
    }

    private void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } &&
            Enum.TryParse<AppThemePreference>(tag, true, out var preference))
        {
            ApplyThemePreference(preference, true);
        }
    }

    private void ApplyThemePreference(AppThemePreference preference, bool persist)
    {
        _themePreference = preference;
        var elementTheme = ThemePreferenceStore.ToElementTheme(preference);
        RootGrid.RequestedTheme = elementTheme;
        _aboutWindow?.ApplyTheme(elementTheme);
        UpdateThemeMenuChecks(preference);
        if (persist)
        {
            ThemePreferenceStore.Save(preference);
        }
    }

    private void UpdateThemeMenuChecks(AppThemePreference preference)
    {
        var system = preference == AppThemePreference.System;
        var light = preference == AppThemePreference.Light;
        var dark = preference == AppThemePreference.Dark;
        SystemThemeMenuItem.IsChecked = system;
        LightThemeMenuItem.IsChecked = light;
        DarkThemeMenuItem.IsChecked = dark;
        FallbackSystemThemeMenuItem.IsChecked = system;
        FallbackLightThemeMenuItem.IsChecked = light;
        FallbackDarkThemeMenuItem.IsChecked = dark;
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_aboutWindow is not null)
            {
                _aboutWindow.Activate();
                return;
            }

            _aboutWindow = new AboutWindow(this, RootGrid.ActualTheme);
            _aboutWindow.Closed += (_, _) => _aboutWindow = null;
            _aboutWindow.Activate();
        }
        catch (Exception error)
        {
            _aboutWindow = null;
            ShowError("Unable to open About", error.Message);
        }
    }

    private void ConfigureKeyboardAccelerators()
    {
        AddKeyboardAccelerator(VirtualKey.O, VirtualKeyModifiers.Control,
            () => OpenPdb_Click(this, new RoutedEventArgs()));
        AddKeyboardAccelerator(VirtualKey.F, VirtualKeyModifiers.Control,
            () => SearchBox.Focus(FocusState.Keyboard));
        AddKeyboardAccelerator(VirtualKey.Left, VirtualKeyModifiers.Menu,
            () => Back_Click(this, new RoutedEventArgs()));
        AddKeyboardAccelerator(VirtualKey.Right, VirtualKeyModifiers.Menu,
            () => Forward_Click(this, new RoutedEventArgs()));
        AddKeyboardAccelerator(VirtualKey.F1, VirtualKeyModifiers.None,
            () => About_Click(this, new RoutedEventArgs()));
    }

    private void AddKeyboardAccelerator(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, args) =>
        {
            args.Handled = true;
            action();
        };
        RootGrid.KeyboardAccelerators.Add(accelerator);
    }
    private void PaneSplitter_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        PaneSplitter.Background = new SolidColorBrush(Color.FromArgb(80, 128, 128, 128));
    }

    private void PaneSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _resizingPane = true;
        _paneStartX = e.GetCurrentPoint(RootGrid).Position.X;
        _paneStartWidth = LeftPaneColumn.ActualWidth;
        PaneSplitter.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void PaneSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizingPane)
        {
            return;
        }

        var delta = e.GetCurrentPoint(RootGrid).Position.X - _paneStartX;
        var width = Math.Clamp(_paneStartWidth + delta, 300, 560);
        LeftPaneColumn.Width = new GridLength(width);
        e.Handled = true;
    }

    private void PaneSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizingPane)
        {
            return;
        }

        _resizingPane = false;
        PaneSplitter.ReleasePointerCapture(e.Pointer);
        PaneSplitter.Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"];
        e.Handled = true;
    }

    private void PaneSplitter_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _resizingPane = false;
        if (Application.Current.Resources.TryGetValue("DividerStrokeColorDefaultBrush", out var resource) &&
            resource is Brush brush)
        {
            PaneSplitter.Background = brush;
        }
    }
    private async Task InitializeAsync()
    {
        if (_workspaceService is null)
        {
            _initialized = true;
            return;
        }

        _initialized = true;

        await ReloadSymbolsAsync(true);
        if (_initialPdbPaths.Count > 0)
        {
            await OpenPdbPathsAsync(_initialPdbPaths);
        }
        UpdateMemoryUsage();
    }

    private async void OpenPdb_Click(object sender, RoutedEventArgs e)
    {
        if (_workspaceService is null)
        {
            return;
        }

        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".pdb");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var files = await picker.PickMultipleFilesAsync();
        if (files.Count == 0)
        {
            return;
        }

        await OpenPdbPathsAsync(files.Select(file => file.Path));
    }

    private async Task OpenPdbPathsAsync(IEnumerable<string> paths)
    {
        if (_workspaceService is null)
        {
            return;
        }

        var openedAny = false;
        SetBusy(true);
        try
        {
            foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                var info = await _workspaceService.OpenPdbAsync(path);
                _pdbs.Add(info);
                _pdbFilters.Add(new PdbFilterOption(info.PdbId, info.DisplayName));
                openedAny = true;
            }

            if (!openedAny)
            {
                return;
            }

            _suppressControlEvents = true;
            PdbFilterBox.SelectedIndex = 0;
            _suppressControlEvents = false;
            ClearDefinition();
            await ReloadSymbolsAsync(true);
            StatusText.Text = $"Loaded {_pdbs.Count:N0} PDB file(s)";
            TitleSubtitleText.Text = _pdbs.Count == 1
                ? _pdbs[0].Name
                : $"{_pdbs.Count:N0} PDB files loaded";
        }
        catch (Exception error)
        {
            ShowError("Failed to open PDB", error.Message);
        }
        finally
        {
            SetBusy(false);
            UpdateMemoryUsage();
        }
    }
    private async void ManagePdbs_Click(object sender, RoutedEventArgs e)
    {
        if (_workspaceService is null || _manageDialogOpen)
        {
            return;
        }

        _manageDialogOpen = true;
        try
        {
            var dialog = new PdbManagerDialog(this, _pdbs)
            {
                XamlRoot = RootGrid.XamlRoot
            };
            await dialog.ShowAsync();

            if (dialog.RemovedPdbIds.Count > 0)
            {
                await ClosePdbIdsAsync(dialog.RemovedPdbIds);
            }

            if (dialog.AddedPaths.Count > 0)
            {
                await OpenPdbPathsAsync(dialog.AddedPaths);
            }
        }
        catch (Exception error)
        {
            ShowError("Unable to manage PDBs", error.Message);
        }
        finally
        {
            _manageDialogOpen = false;
        }
    }

    private async Task ClosePdbIdsAsync(IReadOnlyCollection<uint> pdbIds)
    {
        if (_workspaceService is null || pdbIds.Count == 0)
        {
            return;
        }

        SetBusy(true);
        try
        {
            foreach (var pdbId in pdbIds.Distinct())
            {
                await _workspaceService.ClosePdbAsync(pdbId);
                var pdb = _pdbs.FirstOrDefault(value => value.PdbId == pdbId);
                if (pdb is not null)
                {
                    _pdbs.Remove(pdb);
                }

                var option = _pdbFilters.FirstOrDefault(value => value.PdbId == pdbId);
                if (option is not null)
                {
                    _pdbFilters.Remove(option);
                }
            }

            _suppressControlEvents = true;
            PdbFilterBox.SelectedIndex = 0;
            _suppressControlEvents = false;
            ClearDefinition();
            await ReloadSymbolsAsync(true);
            TitleSubtitleText.Text = _pdbs.Count == 0
                ? "PDB symbol explorer"
                : _pdbs.Count == 1 ? _pdbs[0].Name : $"{_pdbs.Count:N0} PDB files loaded";
        }
        catch (Exception error)
        {
            ShowError("Failed to close PDB", error.Message);
        }
        finally
        {
            SetBusy(false);
            UpdateMemoryUsage();
        }
    }

    private void RootGrid_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private async void RootGrid_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        var paths = items.OfType<StorageFile>()
            .Select(file => file.Path)
            .Where(path => path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (paths.Length > 0)
        {
            await OpenPdbPathsAsync(paths);
        }
    }
    private async void PdbFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ignoreNextPdbFilterChange)
        {
            _ignoreNextPdbFilterChange = false;
            return;
        }
        if (!_initialized || _suppressControlEvents)
        {
            return;
        }
        await ReloadSymbolsAsync(true);
    }

    private async void CategoryFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _suppressControlEvents)
        {
            return;
        }
        await ReloadSymbolsAsync(true);
    }

    private async void SearchBox_TextChanged(object sender, AutoSuggestBoxTextChangedEventArgs e)
    {
        if (_ignoreNextSearchChange)
        {
            _ignoreNextSearchChange = false;
            return;
        }
        if (!_initialized || _suppressControlEvents)
        {
            return;
        }

        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        var token = _searchCancellation.Token;

        try
        {
            await Task.Delay(220, token);
            await ReloadSymbolsAsync(true, token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async void LoadMore_Click(object sender, RoutedEventArgs e)
    {
        await ReloadSymbolsAsync(false);
    }

    private async Task ReloadSymbolsAsync(bool reset, CancellationToken cancellationToken = default)
    {
        if (_workspaceService is null || !_initialized)
        {
            return;
        }

        var generation = ++_queryGeneration;
        SetBusy(true);
        try
        {
            if (reset)
            {
                _symbols.Clear();
                _currentOffset = 0;
                _hasMore = false;
            }

            var page = await _workspaceService.QuerySymbolsAsync(
                SelectedPdbId,
                SelectedCategories,
                SearchBox.Text.Trim(),
                _currentOffset,
                PageSize,
                cancellationToken);

            if (generation != _queryGeneration)
            {
                return;
            }

            foreach (var item in page.Items)
            {
                _symbols.Add(item);
            }
            TrimSymbolWindow();

            _currentOffset += (uint)page.Items.Count;
            _hasMore = page.HasMore;
            LoadMoreButton.Visibility = _hasMore ? Visibility.Visible : Visibility.Collapsed;
            SymbolCountText.Text = $"{page.Total:N0} symbols";
            StatusText.Text = $"{page.Total:N0} symbol(s) match the current filter";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            ShowError("Symbol query failed", error.Message);
        }
        finally
        {
            if (generation == _queryGeneration)
            {
                SetBusy(false);
            }
            UpdateMemoryUsage();
        }
    }

    private async void SymbolsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionLoading || SymbolsList.SelectedItem is not SymbolItem item)
        {
            return;
        }

        if (_currentDefinition is not null &&
            (_currentDefinition.PdbId != item.PdbId || _currentDefinition.SymIndexId != item.SymIndexId))
        {
            _backHistory.Push(new SymbolKey(_currentDefinition.PdbId, _currentDefinition.SymIndexId));
            _forwardHistory.Clear();
        }

        await LoadDefinitionAsync(item.PdbId, item.SymIndexId);
        UpdateHistoryButtons();
    }

    private async Task LoadDefinitionAsync(uint pdbId, uint symIndexId)
    {
        if (_workspaceService is null)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var definition = await _workspaceService.GetDefinitionAsync(pdbId, symIndexId);
            _currentDefinition = definition;
            var pdbName = _pdbs.FirstOrDefault(value => value.PdbId == pdbId)?.Name ?? $"PDB {pdbId}";
            BreadcrumbText.Text = $"{pdbName} / {definition.Name}";
            SymbolTitle.Text = definition.Name;
            SymbolMetadata.Text = $"{definition.DisplayKind}  ·  {pdbName}";
            CopyButton.IsEnabled = true;
            DefinitionHeader.Visibility = Visibility.Visible;

            RenderCurrentDefinition();

            StatusText.Text = $"Selected {definition.Name}";
        }
        catch (Exception error)
        {
            ShowError("Failed to load symbol definition", error.Message);
        }
        finally
        {
            SetBusy(false);
            UpdateMemoryUsage();
        }
    }

    private void ClearDefinition()
    {
        _currentDefinition = null;
        CopyButton.IsEnabled = false;
        BreadcrumbText.Text = "No symbol selected";
        SymbolTitle.Text = "Open a PDB to begin";
        SymbolMetadata.Text = string.Empty;
        DefinitionHeader.Visibility = Visibility.Collapsed;
        CodeLineNumbers.Text = string.Empty;
        CodeRichText.Blocks.Clear();
        CodeSurface.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Visible;
    }
    private void RenderCurrentDefinition()
    {
        if (_currentDefinition is null)
        {
            return;
        }

        var darkTheme = RootGrid.ActualTheme != ElementTheme.Light;
        CodeDocumentRenderer.Render(
            CodeRichText,
            CodeLineNumbers,
            _currentDefinition.Code,
            _currentDefinition.Links,
            darkTheme,
            link => NavigateToSymbolAsync(link.TargetPdbId, link.TargetSymIndexId, link.TargetKind, true));
        CodeSurface.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;
    }
    private async Task NavigateToSymbolAsync(uint pdbId, uint symIndexId, string kind, bool addHistory)
    {
        if (_workspaceService is null)
        {
            return;
        }

        if (addHistory && _currentDefinition is not null &&
            (_currentDefinition.PdbId != pdbId || _currentDefinition.SymIndexId != symIndexId))
        {
            _backHistory.Push(new SymbolKey(_currentDefinition.PdbId, _currentDefinition.SymIndexId));
            _forwardHistory.Clear();
        }

        try
        {
            _suppressControlEvents = true;
            var pdbOption = _pdbFilters.FirstOrDefault(value => value.PdbId == pdbId);
            if (pdbOption is not null && !ReferenceEquals(PdbFilterBox.SelectedItem, pdbOption))
            {
                _ignoreNextPdbFilterChange = true;
                PdbFilterBox.SelectedItem = pdbOption;
            }
            EnsureCategorySelected(kind);
            if (!string.IsNullOrEmpty(SearchBox.Text))
            {
                _ignoreNextSearchChange = true;
                SearchBox.Text = string.Empty;
            }

            var categories = SelectedCategories;
            var located = await _workspaceService.LocateSymbolAsync(
                pdbId, pdbId, symIndexId, categories);
            var pageOffset = (uint)(Math.Max(0, located.Ordinal) / PageSize * PageSize);
            var page = await _workspaceService.QuerySymbolsAsync(
                pdbId, categories, string.Empty, pageOffset, PageSize);

            _queryGeneration++;
            _symbols.Clear();
            foreach (var item in page.Items)
            {
                _symbols.Add(item);
            }
            _currentOffset = pageOffset + (uint)page.Items.Count;
            _hasMore = page.HasMore;
            LoadMoreButton.Visibility = _hasMore ? Visibility.Visible : Visibility.Collapsed;

            var target = _symbols.FirstOrDefault(item =>
                item.PdbId == pdbId && item.SymIndexId == symIndexId);
            _suppressSelectionLoading = true;
            SymbolsList.SelectedItem = target;
            _suppressSelectionLoading = false;

            if (target is not null)
            {
                SymbolsList.ScrollIntoView(target);
            }

            await LoadDefinitionAsync(pdbId, symIndexId);
            UpdateHistoryButtons();
        }
        catch (Exception error)
        {
            ShowError("Failed to navigate to symbol", error.Message);
        }
        finally
        {
            _suppressControlEvents = false;
            _suppressSelectionLoading = false;
        }
    }

    private async void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_backHistory.Count == 0)
        {
            return;
        }

        var target = _backHistory.Pop();
        if (_currentDefinition is not null)
        {
            _forwardHistory.Push(new SymbolKey(_currentDefinition.PdbId, _currentDefinition.SymIndexId));
        }
        await NavigateToSymbolAsync(target.PdbId, target.SymIndexId, string.Empty, false);
    }

    private async void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (_forwardHistory.Count == 0)
        {
            return;
        }

        var target = _forwardHistory.Pop();
        if (_currentDefinition is not null)
        {
            _backHistory.Push(new SymbolKey(_currentDefinition.PdbId, _currentDefinition.SymIndexId));
        }
        await NavigateToSymbolAsync(target.PdbId, target.SymIndexId, string.Empty, false);
    }

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDefinition is null)
        {
            return;
        }

        try
        {
            var package = new DataPackage();
            package.SetText(_currentDefinition.Code);
            Clipboard.SetContent(package);
            try
            {
                // Persisting the clipboard can fail in restricted desktop sessions,
                // but the content is already available for the current session.
                Clipboard.Flush();
            }
            catch
            {
            }
            StatusText.Text = "Copied C/C++ definition to the clipboard";
            return;
        }
        catch
        {
            // Fall back to the Win32 clipboard API when WinRT clipboard access
            // is unavailable in the current desktop session.
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (UnicodeClipboard.TrySetText(_currentDefinition.Code))
            {
                StatusText.Text = "Copied C/C++ definition to the clipboard";
                return;
            }
            await Task.Delay(80);
        }

        ShowError("Copy failed", "The clipboard is unavailable.");
    }

    private void TrimSymbolWindow()
    {
        while (_symbols.Count > MaxRetainedSymbols)
        {
            _symbols.RemoveAt(0);
        }
    }

    private uint SelectedPdbId => PdbFilterBox.SelectedItem is PdbFilterOption option ? option.PdbId : 0;

    private uint SelectedCategories
    {
        get
        {
            uint categories = 0;
            if (StructFilter.IsChecked == true) categories |= SymbolCategoryFlags.Struct;
            if (ClassFilter.IsChecked == true) categories |= SymbolCategoryFlags.Class;
            if (UnionFilter.IsChecked == true) categories |= SymbolCategoryFlags.Union;
            if (EnumFilter.IsChecked == true) categories |= SymbolCategoryFlags.Enum;
            if (TypedefFilter.IsChecked == true) categories |= SymbolCategoryFlags.Typedef;
            if (FunctionFilter.IsChecked == true) categories |= SymbolCategoryFlags.Function;
            if (DataFilter.IsChecked == true) categories |= SymbolCategoryFlags.Data;
            if (PublicSymbolFilter.IsChecked == true) categories |= SymbolCategoryFlags.PublicSymbol;
            return categories;
        }
    }

    private void EnsureCategorySelected(string kind)
    {
        var flag = SymbolCategoryFlags.FromKind(kind);
        if (flag == SymbolCategoryFlags.Struct) StructFilter.IsChecked = true;
        else if (flag == SymbolCategoryFlags.Class) ClassFilter.IsChecked = true;
        else if (flag == SymbolCategoryFlags.Union) UnionFilter.IsChecked = true;
        else if (flag == SymbolCategoryFlags.Enum) EnumFilter.IsChecked = true;
        else if (flag == SymbolCategoryFlags.Typedef) TypedefFilter.IsChecked = true;
        else if (flag == SymbolCategoryFlags.Function) FunctionFilter.IsChecked = true;
        else if (flag == SymbolCategoryFlags.Data) DataFilter.IsChecked = true;
        else if (flag == SymbolCategoryFlags.PublicSymbol) PublicSymbolFilter.IsChecked = true;
    }

    private void UpdateHistoryButtons()
    {
        var canGoBack = _backHistory.Count > 0;
        var canGoForward = _forwardHistory.Count > 0;
        BackButton.IsEnabled = canGoBack;
        ForwardButton.IsEnabled = canGoForward;
        FallbackBackButton.IsEnabled = canGoBack;
        FallbackForwardButton.IsEnabled = canGoForward;
    }

    private void SetBusy(bool busy)
    {
        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowError(string title, string message)
    {
        MessageBar.Title = title;
        MessageBar.Message = message;
        MessageBar.Severity = InfoBarSeverity.Error;
        MessageBar.IsOpen = true;
        StatusText.Text = message;
    }

    private void UpdateMemoryUsage()
    {
        using var process = Process.GetCurrentProcess();
        MemoryText.Text = $"Working set: {process.WorkingSet64 / (1024d * 1024d):N0} MB";
    }

    private async void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        if (_workspaceService is not null)
        {
            await _workspaceService.DisposeAsync();
        }
    }

    private readonly record struct SymbolKey(uint PdbId, uint SymIndexId);
}
