using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;
public partial class DisplayFilterEditorView : UserControl
{
    private LauncherSession _session = null!;
    private IFeatureNavigationHost _host = null!;
    private Func<string, string, Task> _message = null!;
    private Action _dismissInput = null!;
    private int _generation;
    public DisplayFilterEditorViewModel Model { get; private set; } = null!;
    public FeatureFormNavigation Navigation { get; private set; } = null!;

    public event Action? CloseRequested;
    public event Action<bool>? Saved;
    public DisplayFilterEditorView()
    {
        InitializeComponent();
        GamepadComboBoxNavigation.Attach(DisplayFilterMatchModeComboBox);
        GamepadComboBoxNavigation.Attach(DisplayFilterExcludeMatchModeComboBox);
    }

    public void Configure(SettingsViewModel settings, LauncherSession session, IFeatureNavigationHost host, Func<string, string, Task> message, Action dismissInput)
    {
        Model = new(settings);
        DataContext = Model;
        _session = session;
        _host = host;
        _message = message;
        _dismissInput = dismissInput;
        Navigation = new(this, () => [DisplayFilterNameTextBox, DisplayFilterTagsTextBox, DisplayFilterMatchModeComboBox, DisplayFilterExcludeTagsTextBox, DisplayFilterExcludeMatchModeComboBox, CancelDisplayFilterButton, SaveDisplayFilterButton], host, GamepadNavigationZone.DisplayFilterOverlay, () => CloseRequested?.Invoke());
    }

    public bool Open(string? filterId)
    {
        if (_session.IsClosed || !Model.Open(filterId))
            return false;
        var generation = ++_generation;
        Dispatcher.UIThread.Post(() =>
        {
            if (_session.IsClosed || !IsVisible || generation != _generation)
                return;
            if (_host.IsFocusActive)
                Navigation.ApplySelection(0);
            else
            {
                Navigation.ClearFocus();
                _dismissInput();
            }
        }, DispatcherPriority.Loaded);
        return true;
    }

    public void CloseEditor()
    {
        ++_generation;
        Navigation.ClearFocus();
        Navigation.FocusIndex = -1;
        Model.Close();
    }

    private void CancelDisplayFilterEdit_Click(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke();
    private async void SaveDisplayFilter_Click(object? sender, RoutedEventArgs e)
    {
        await _session.RunAsync(async () =>
        {
            try
            {
                var result = Model.Save();
                if (!result.Saved)
                {
                    if (result.Error != null)
                        await _message(result.Error, result.ErrorTitle!);
                    return;
                }

                CloseRequested?.Invoke();
                Saved?.Invoke(result.WasEdit);
            }
            catch (Exception ex)when (!_session.IsClosed)
            {
                await _message($"Failed to save settings: {ex.Message}", "Save Error");
            }
        });
    }
}
