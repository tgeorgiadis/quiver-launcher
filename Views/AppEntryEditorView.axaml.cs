using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using QuiverLauncher.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;
public partial class AppEntryEditorView : UserControl
{
    private LauncherSession _session = null!;
    private IFeatureNavigationHost _host = null!;
    private Func<Task> _reloadLibrary = null!;
    private Action _dismissTextInput = null!;
    private Func<string, string, Task> _showMessage = null!;
    private int _generation;
    private Task _notices = Task.CompletedTask;
    public AppEntryEditorViewModel Model { get; } = new();
    public FeatureFormNavigation Navigation { get; private set; } = null!;

    public event Action? CloseRequested;
    public event Action<string>? EntryCreated;
    public AppEntryEditorView()
    {
        InitializeComponent();
        DataContext = Model;
        SizeChanged += (_, _) => FitAvailableHeight();
        GamepadComboBoxNavigation.Attach(NewGameRepositorySourceComboBox);
    }

    public void Configure(LauncherSession session, IFeatureNavigationHost host, IAppEntryService service, Func<Task> reloadLibrary, Func<string, string, Task> showMessage, Action<GameInfo> openFolder, Action dismissTextInput)
    {
        _session = session;
        _host = host;
        _reloadLibrary = reloadLibrary;
        _dismissTextInput = dismissTextInput;
        _showMessage = showMessage;
        Model.Configure(service);
        Model.Notice += notice =>
        {
            var previous = _notices;
            _notices = session.RunAsync(async () =>
            {
                await previous;
                if (!session.IsClosed) await showMessage(notice.Message, notice.Title);
            });
        };
        Model.OpenFolderRequested += openFolder;
        Navigation = new FeatureFormNavigation(this, () => [NewGameNameTextBox, NewGameProjectTextBox, NewGameCustomDisplayNameTextBox, NewGameManuallyManagedCheckBox, NewGameRepositorySourceComboBox, NewGameRepoTextBox, NewGameReleaseAssetFilterTextBox, NewGameFolderTextBox, NewGameTagsTextBox, NewGameIconTextBox, NewGameFilesToAddTextBox, NewGameModsPathTextBox, NewGameModsFolderPerModCheckBox, NewGameModsSourcesTextBox, CancelButton, CreateEditButton], host, GamepadNavigationZone.EntryFormOverlay, () => CloseRequested?.Invoke());
    }

    public void Open(GameInfo? game = null)
    {
        var generation = ++_generation;
        Model.Open(game);
        foreach (var item in NewGameRepositorySourceComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, Model.RepositorySource, StringComparison.OrdinalIgnoreCase))
            {
                NewGameRepositorySourceComboBox.SelectedItem = item;
                break;
            }
        }

        var gaming = SteamDeckEnvironment.IsGamingMode();
        TagEditOverlayLayout.ApplyDialogPlacement(EntryFormDialogPanel, gaming);
        if (!gaming)
            ResetPlacement();
        FitAvailableHeight();
        Dispatcher.UIThread.Post(() =>
        {
            if (_session.IsClosed || !IsVisible || generation != _generation)
                return;
            FitAvailableHeight();
            if (_host.IsFocusActive)
                Navigation.ApplySelection(0);
            else
            {
                Navigation.ClearFocus();
                _dismissTextInput();
            }
        }, DispatcherPriority.Loaded);
    }

    private void ResetPlacement()
    {
        EntryFormDialogPanel.ClearValue(Layoutable.MarginProperty);
        EntryFormDialogPanel.ClearValue(Layoutable.VerticalAlignmentProperty);
    }

    public void CloseEditor()
    {
        ++_generation;
        Navigation.ClearFocus();
        Navigation.FocusIndex = -1;
        Model.Close();
        ResetPlacement();
    }

    public void FitAvailableHeight()
    {
        if (!IsVisible || EntryFormFieldsScroll == null)
            return;
        var available = Bounds.Height;
        if (available <= 1)
            available = TopLevel.GetTopLevel(this)?.ClientSize.Height ?? 0;
        var maxHeight = EntryFormOverlayLayout.ResolveScrollMaxHeight(available, fillAvailable: PlatformCapabilities.IsMobile);
        if (maxHeight is null)
            EntryFormFieldsScroll.ClearValue(Layoutable.MaxHeightProperty);
        else
            EntryFormFieldsScroll.MaxHeight = maxHeight.Value;
    }

    private void RepositorySource_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (NewGameRepositorySourceComboBox?.SelectedItem is ComboBoxItem { Tag: string tag })
            Model.RepositorySource = RepositorySourceHelper.Normalize(tag);
    }

    private void CancelForm_Click(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke();
    internal async void CreateNewEntry_Click(object? sender, RoutedEventArgs e)
    {
        var generation = _generation;
        var creating = Model.IsCreating;
        var folder = Model.FolderName.Trim();
        await _session.RunAsync(async () =>
        {
            var saved = await Model.SaveAsync(_session.Token);
            // Dialogs retain navigation ownership until dismissed; only then reveal the card.
            await _notices;
            if (!saved || _session.IsClosed || generation != _generation)
                return;
            try
            {
                await _reloadLibrary();
                if (!_session.IsClosed && generation == _generation)
                {
                    CloseRequested?.Invoke();
                    if (creating) EntryCreated?.Invoke(folder);
                }
            }
            catch (Exception ex)
            {
                if (!_session.IsClosed)
                    await _showMessage($"Error saving app entry: {ex.Message}", "Error");
            }
        });
    }
}
