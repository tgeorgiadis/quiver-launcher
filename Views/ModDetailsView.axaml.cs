using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using QuiverLauncher.Services;
using QuiverLauncher.Services.Mods;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;
public partial class ModDetailsView : UserControl, IFeatureNavigationHandler
{
    private ModsFeatureContext _context = null!;
    private IModsFeatureHost _host = null!;
    private GamepadNavigationService _gamepadNavigation => _host.Navigation;

    internal int FocusIndex = -1;
    private int _openGeneration;
    public ModDetailsViewModel Model { get; } = new();

    public event Action? Closed;
    public bool Navigate(NavigationDirection direction) => HandleModDetailsGamepadNavigation(direction);
    public bool Confirm()
    {
        HandleModDetailsGamepadConfirm();
        return true;
    }

    public bool Cancel()
    {
        Close();
        return true;
    }

    public bool Options() => Cancel();
    public void RestoreFocus() => ApplyModDetailsGamepadSelection(Math.Max(0, FocusIndex));
    public ModDetailsView()
    {
        InitializeComponent();
        DataContext = Model;
    }

    public void Configure(ModsFeatureContext context, IModsFeatureHost host)
    {
        _context = context;
        _host = host;
        Model.PropertyChanged += ModelChanged;
        context.Session.OnShutdown(() =>
        {
            Model.PropertyChanged -= ModelChanged;
            Model.Dispose();
        });
    }

    private void ModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_context.Session.IsClosed)
            return;
        if (e.PropertyName == nameof(Model.Tab))
        {
            ModDetailsTabDetailsButton.Classes.Set("selected", Model.Tab == "Details");
            ModDetailsTabChangelogButton.Classes.Set("selected", Model.Tab == "Changelog");
        }

        if (e.PropertyName == nameof(Model.IsLoading) && !Model.IsLoading)
            ModDetailsScrollViewer.Offset = new Vector(0, 0);
        if (e.PropertyName != nameof(Model.Content))
            return;
        var content = Model.Content;
        ModDetailsContent.ItemsSource = content.IsMarkdown ? _context.Renderer.Render(content.Text) : new Control[]
        {
            new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = content.Text,
                        FontSize = 14,
                        Foreground = new SolidColorBrush(Color.Parse("#B8B8B8"))
                    }
                }
            }
        };
    }

    public async Task OpenAsync(ModListItem item)
    {
        if (_context.Session.IsClosed)
            return;
        var generation = ++_openGeneration;
        _context.Shell.ModDetailsOpen = true;
        IsVisible = true;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.ModsDetailsOverlay;
        _host.NotifyHints();
        var load = Model.OpenAsync(item, LoadAsync, _context.Session.Token);
        if (_host.IsFocusActive)
            Dispatcher.UIThread.Post(() =>
            {
                if (!_context.Session.IsClosed && generation == _openGeneration && Model.Tab == "Details")
                    ApplyModDetailsGamepadSlot(ModDetailsGamepadSlot.Details);
            }, DispatcherPriority.Loaded);
        await load;
        if (_context.Session.IsClosed || generation != _openGeneration || Model.Tab != "Details")
            return;
        if (_host.IsFocusActive)
            ApplyModDetailsGamepadSlot(ModDetailsGamepadSlot.Details);
        else
            ClearModDetailsGamepadFocus();
    }

    private Task<string?> LoadAsync(ModPackage package, bool changelog, CancellationToken token)
    {
        if (!_context.Games.ModProviderRegistry.TryGet(package.ProviderId, out var provider))
            throw new NotSupportedException();
        return changelog ? provider.GetChangelogAsync(package, token) : provider.GetReadmeAsync(package, token);
    }

    public void Close()
    {
        ++_openGeneration;
        Model.Close();
        ClearModDetailsGamepadFocus();
        FocusIndex = -1;
        _context.Shell.ModDetailsOpen = false;
        IsVisible = false;
        ModDetailsContent.ItemsSource = null;
        if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.ModsDetailsOverlay)
            Closed?.Invoke();
        _host.NotifyHints();
    }

    private void CloseModDetails_Click(object? sender, RoutedEventArgs e) => Close();
    private void ModDetailsOpenPage_Click(object? sender, RoutedEventArgs e)
    {
        if (!_context.Session.IsClosed && Model.Item?.Package.PackagePageUrl is { Length: > 0 } url)
            _host.OpenUrl(url);
    }

    private async void ModDetailsTab_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tab } && tab != Model.Tab)
            await _context.Session.RunAsync(() => Model.SelectTabAsync(tab));
    }

    private bool TryApplyGamepadZoneTransition(GamepadZoneTransition transition) => _host.ApplyTransition(transition);
    private static void ActivateFocusedControl(IReadOnlyList<Control> controls, int index)
    {
        if (index >= 0 && index < controls.Count && controls[index] is Button button)
            GamepadControlActivation.ActivateButton(button);
    }

    private static void ClearStyledControlsGamepadFocusClasses(IReadOnlyList<Control> controls)
    {
        foreach (var control in controls)
            control.Classes.Remove("gamepad-focused");
    }

    internal bool HandleModDetailsGamepadNavigation(NavigationDirection direction)
    {
        var current = GetModDetailsGamepadSlot();
        if (current == null)
        {
            ApplyModDetailsGamepadSlot(ModDetailsGamepadSlot.Details);
            return true;
        }

        var move = ModDetailsGamepadNavigation.Move(current.Value, direction, openPageVisible: ModDetailsOpenPageButton is { IsVisible: true, IsEnabled: true });
        if (move.LeaveZone is { } leave)
            return TryApplyGamepadZoneTransition(new GamepadZoneTransition(leave, null));
        if (move.ScrollBody)
        {
            var scrollViewer = ModDetailsScrollViewer;
            if (scrollViewer != null)
            {
                const double step = 96;
                scrollViewer.Offset = new Vector(scrollViewer.Offset.X, scrollViewer.Offset.Y + step);
            }

            return true;
        }

        if (move.Slot is { } slot)
            ApplyModDetailsGamepadSlot(slot);
        return true;
    }

    internal ModDetailsGamepadSlot? GetModDetailsGamepadSlot()
    {
        var controls = CollectModDetailsFocusableControls();
        var index = _gamepadNavigation.ClampIndex(FocusIndex, controls.Count);
        if (index < 0 || index >= controls.Count)
            return null;
        var control = controls[index];
        if (ReferenceEquals(control, ModDetailsOpenPageButton))
            return ModDetailsGamepadSlot.OpenPage;
        if (ReferenceEquals(control, CloseModDetailsButton))
            return ModDetailsGamepadSlot.Close;
        if (ReferenceEquals(control, ModDetailsTabDetailsButton))
            return ModDetailsGamepadSlot.Details;
        if (ReferenceEquals(control, ModDetailsTabChangelogButton))
            return ModDetailsGamepadSlot.Changelog;
        return null;
    }

    internal void ApplyModDetailsGamepadSlot(ModDetailsGamepadSlot slot)
    {
        var control = slot switch
        {
            ModDetailsGamepadSlot.OpenPage => ModDetailsOpenPageButton,
            ModDetailsGamepadSlot.Close => CloseModDetailsButton,
            ModDetailsGamepadSlot.Details => ModDetailsTabDetailsButton,
            ModDetailsGamepadSlot.Changelog => ModDetailsTabChangelogButton,
            _ => null,
        };
        var controls = CollectModDetailsFocusableControls();
        var index = control != null ? controls.IndexOf(control) : -1;
        if (index < 0)
        {
            index = controls.IndexOf(ModDetailsTabDetailsButton);
            if (index < 0)
                index = 0;
        }

        ApplyModDetailsGamepadSelection(index);
    }

    internal void ApplyModDetailsGamepadSlotFromChrome(GamepadNavigationZone fromZone)
    {
        var openPageVisible = ModDetailsOpenPageButton is { IsVisible: true, IsEnabled: true };
        ApplyModDetailsGamepadSlot(ModDetailsGamepadNavigation.SlotReturningFromChrome(fromZone, openPageVisible));
    }

    internal void HandleModDetailsGamepadConfirm()
    {
        var controls = CollectModDetailsFocusableControls();
        var index = _gamepadNavigation.ClampIndex(FocusIndex, controls.Count);
        if (index < 0 || index >= controls.Count)
            return;
        ActivateFocusedControl(controls, index);
    }

    internal List<Control> CollectModDetailsFocusableControls()
    {
        var list = new List<Control>();
        void Add(Control? c)
        {
            if (c != null && c.IsVisible && c.IsEnabled)
                list.Add(c);
        }

        Add(ModDetailsTabDetailsButton);
        Add(ModDetailsTabChangelogButton);
        Add(ModDetailsOpenPageButton);
        Add(CloseModDetailsButton);
        return list;
    }

    internal void ApplyModDetailsGamepadSelection(int index)
    {
        var controls = CollectModDetailsFocusableControls();
        index = _gamepadNavigation.ClampIndex(index, controls.Count);
        FocusIndex = index;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.ModsDetailsOverlay;
        ClearModDetailsGamepadFocus();
        if (index < 0 || index >= controls.Count)
            return;
        if (controls[index] is StyledElement styled)
            styled.Classes.Set("gamepad-focused", true);
        GamepadControlActivation.ApplyGamepadHighlightFocus(controls[index]);
    }

    internal void ClearModDetailsGamepadFocus() => ClearStyledControlsGamepadFocusClasses(CollectModDetailsFocusableControls());
    public bool SynchronizePointer(object? source) => GamepadPointerFocusSync.Hit(_gamepadNavigation, CollectModDetailsFocusableControls(), GamepadNavigationZone.ModsDetailsOverlay, FocusIndex, ApplyModDetailsGamepadSelection, source);
    public void LeaveZone(GamepadNavigationZone nextZone)
    {
        if (nextZone != GamepadNavigationZone.ModsDetailsOverlay)
            ClearModDetailsGamepadFocus();
    }

    public bool EnterZone(GamepadZoneTransition transition)
    {
        switch (transition.Zone)
        {
            case GamepadNavigationZone.ModsDetailsOverlay:
                ApplyModDetailsGamepadSlotFromChrome(_gamepadNavigation.ActiveZone);
                return true;
            default:
                return false;
        }
    }
}
