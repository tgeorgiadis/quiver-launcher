using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;
public partial class MetadataEditorView : UserControl
{
    private LauncherSession _session = null!;
    private IFeatureNavigationHost _host = null!;
    private Func<Task> _reloadLibrary = null!;
    private Func<string, Task> _showError = null!;
    private Action _dismissTextInput = null!;
    private int _openGeneration;
    public MetadataEditorViewModel Model { get; } = new();
    public FeatureFormNavigation Navigation { get; private set; } = null!;

    public event Action? CloseRequested;
    public MetadataEditorView()
    {
        InitializeComponent();
        DataContext = Model;
    }

    public void Configure(LauncherSession session, IFeatureNavigationHost host, LibraryMetadataService service, Func<Task> reloadLibrary, Func<string, Task> showError, Action dismissTextInput)
    {
        _session = session;
        _host = host;
        _reloadLibrary = reloadLibrary;
        _showError = showError;
        _dismissTextInput = dismissTextInput;
        Model.Configure(service.SaveAsync);
        Navigation = new FeatureFormNavigation(this, () => [TagEditTextBox, TagEditCancelButton, TagEditSaveButton], host, GamepadNavigationZone.TagEditOverlay, () => CloseRequested?.Invoke());
    }

    public void Open(GameInfo game, MetadataEditMode mode)
    {
        var generation = ++_openGeneration;
        Model.Open(game, mode);
        var gaming = SteamDeckEnvironment.IsGamingMode();
        TagEditOverlayLayout.ApplyDialogPlacement(TagEditDialogPanel, gaming);
        Dispatcher.UIThread.Post(() =>
        {
            if (_session.IsClosed || !IsVisible || generation != _openGeneration)
                return;
            if (_host.IsFocusActive)
                Navigation.ApplySelection(TagEditOverlayLayout.GetInitialFocusIndex(gaming));
            else
            {
                Navigation.ClearFocus();
                _dismissTextInput();
            }
        }, DispatcherPriority.Loaded);
    }

    public void CloseEditor()
    {
        ++_openGeneration;
        Model.Close();
        Navigation.ClearFocus();
        Navigation.FocusIndex = -1;
        TagEditOverlayLayout.ApplyDialogPlacement(TagEditDialogPanel, false);
    }

    private void CancelTagEdit_Click(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke();
    private async void SaveTagEdit_Click(object? sender, RoutedEventArgs e)
    {
        await _session.RunAsync(async () =>
        {
            try
            {
                if (!await Model.SaveAsync(_session.Token) || _session.IsClosed)
                    return;
                CloseRequested?.Invoke();
                await _reloadLibrary();
            }
            catch (Exception ex)when (!_session.IsClosed)
            {
                await _showError($"Failed to save: {ex.Message}");
            }
        });
    }
}
