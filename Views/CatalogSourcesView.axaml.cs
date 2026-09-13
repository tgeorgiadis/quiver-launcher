using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;
public partial class CatalogSourcesView : UserControl
{
    private LauncherSession? _session;
    private Func<string, Task> _review = _ => Task.CompletedTask;
    private IFeatureNavigationHost? _host;
    private CancellationToken Token => _session?.Token ?? CancellationToken.None;
    private IStorageProvider StorageProvider => TopLevel.GetTopLevel(this)!.StorageProvider;
    public CatalogViewModel Model { get; private set; } = new();
    public CatalogSourcesNavigation Navigation { get; private set; } = null!;

    public CatalogSourcesView()
    {
        InitializeComponent();
        DataContext = Model;
    }

    public void Configure(CatalogViewModel model, LauncherSession session, IFeatureNavigationHost host, Func<bool> isActive, Func<string, Task> review)
    {
        Model = model;
        DataContext = model;
        _session = session;
        _review = review;
        _host = host;
        Navigation = new(this, host, () => !session.IsClosed && isActive());
        model.ListChanged += OnListChanged;
        session.OnShutdown(() => model.ListChanged -= OnListChanged);
    }

    private void OnListChanged()
    {
        CatalogSourceFilterAllButton.Classes.Set("selected", Model.SourceListFilter == CatalogSourceListFilter.All);
        CatalogSourceFilterEnabledButton.Classes.Set("selected", Model.SourceListFilter == CatalogSourceListFilter.Enabled);
        CatalogSourceFilterDisabledButton.Classes.Set("selected", Model.SourceListFilter == CatalogSourceListFilter.Disabled);
        Navigation.SyncCatalogGamepadSelection();
    }

    public void ApplyMobileLayout()
    {
        CatalogSourcesItemsControl.ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Spacing = 12 });
        CatalogSourcesStack.Margin = new Thickness(0);
    }

    private void Run(Func<Task> operation)
    {
        if (_session != null)
            _ = _session.RunAsync(operation);
    }

    private void CatalogSourceListFilter_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !Enum.TryParse<CatalogSourceListFilter>(tag, out var filter))
            return;
        Model.SourceListFilter = filter;
        Run(() => Model.RefreshAsync(Token));
    }

    private void AddCatalogSource_Click(object? sender, RoutedEventArgs e) => Run(async () =>
    {
        var location = await ShowAddCatalogSourceDialogAsync();
        if (location != null && !Token.IsCancellationRequested)
            await Model.AddAsync(location, Token);
    });
    private void RemoveCatalogSource_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
            Run(() => Model.RemoveAsync(id, Token));
    }

    private void CatalogSourceEnabled_Changed(object? sender, RoutedEventArgs e)
    {
        // Rebuilding the list also changes checkbox bindings. Only a change to a
        // current row, away from its saved value, represents a user toggle.
        if (sender is CheckBox { DataContext: CatalogSourceListItem row, IsChecked: bool enabled }
            && _session != null && Model.Sources.Contains(row) && row.Enabled != enabled)
            Run(() => Model.SetEnabledAsync(row.SourceId, enabled, Token));
    }

    private void RefreshCatalogSources_Click(object? sender, RoutedEventArgs e)
    {
        if (Navigation != null && _host?.IsFocusActive == true)
        {
            var controls = Navigation.CollectCatalogSourcesToolbarControls();
            var index = controls.IndexOf(RefreshCatalogSourcesButton);
            if (index >= 0)
                Navigation.ApplyCatalogSourcesToolbarSelection(index);
        }

        Run(() => Model.RefreshAllAsync(Token));
    }

    private void ReviewCatalogSource_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
            Run(() => _review(id));
    }

    private async Task<string?> ShowAddCatalogSourceDialogAsync()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null)
            return null;
        var locationBox = new TextBox
        {
            Watermark = "URL or file path to apps.json",
            Margin = new Thickness(0, 0, 0, 8),
        };
        locationBox.GotFocus += (_, e) =>
        {
            // Pointer focus keeps click position / selection; gamepad/tab append at end.
            if (e.NavigationMethod != NavigationMethod.Pointer)
                GamepadControlActivation.MoveCaretToEnd(locationBox);
            if (GamepadTextInput.ShouldOpenSteamOskOnGotFocus)
                SteamOnScreenKeyboard.TryOpen();
        };
        var browseButton = new Button
        {
            Content = "Browse…",
            Classes =
            {
                "options"
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 12),
        };
        browseButton.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Select apps.json", FileTypeFilter = new[] { new FilePickerFileType("JSON Files") { Patterns = new[] { "*.json" } } }, AllowMultiple = false, });
            if (files?.Count > 0)
                locationBox.Text = files[0].Path.LocalPath;
        };
        string? result = null;
        var addButton = new Button
        {
            Content = "Add",
            MinWidth = 80,
            Classes =
            {
                "options"
            }
        };
        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 80,
            Classes =
            {
                "options"
            }
        };
        var dialog = new Window
        {
            Title = "Add Catalog Source",
            Width = 420,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    new TextBlock
                    {
                        Text = "Location",
                        FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(0, 0, 0, 4),
                    },
                    locationBox,
                    browseButton,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Spacing = 10,
                        Children =
                        {
                            addButton,
                            cancelButton
                        },
                    },
                },
            },
        };
        addButton.Click += (_, _) =>
        {
            var location = locationBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(location))
                return;
            result = location;
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();
        GamepadModalDialogNavigation.Attach(dialog);
        using var cancellation = Token.Register(() => Dispatcher.UIThread.Post(dialog.Close));
        if (Token.IsCancellationRequested)
            return null;
        DesktopInterfaceScaling.PrepareDialog(dialog, desktop.MainWindow);
        await dialog.ShowDialog(desktop.MainWindow);
        return result;
    }
}
