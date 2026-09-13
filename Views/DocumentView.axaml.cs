using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;
public partial class DocumentView : UserControl, IFeatureNavigationHandler
{
    public bool Navigate(Services.NavigationDirection direction) => HandleChangelogGamepadNavigation(direction);
    public bool Confirm()
    {
        ActivateChangelogGamepadSelection();
        return true;
    }

    public bool Cancel()
    {
        CloseRequested?.Invoke();
        return true;
    }

    public bool Options() => Cancel();
    public void RestoreFocus() => ApplyChangelogGamepadSelection(Math.Max(0, FocusIndex));
    private MarkdownRenderer? _renderer;
    public DocumentViewModel Model { get; } = new();
    public IFeatureNavigationHost NavigationHost { get; set; } = null!;
    public int FocusIndex { get; set; } = -1;

    public event Action? CloseRequested;
    private int _openGeneration;
    public async Task OpenAsync(string title, string loading, Func<CancellationToken, Task<DocumentContent>> load, CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return;
        var generation = ++_openGeneration;
        IsVisible = true;
        NavigationHost.Navigation.ActiveZone = GamepadNavigationZone.ChangelogOverlay;
        var pending = Model.OpenAsync(title, loading, load, token);
        if (NavigationHost.IsFocusActive)
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (!token.IsCancellationRequested && generation == _openGeneration)
                    ApplyChangelogGamepadSelection(0);
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        await pending;
        if (token.IsCancellationRequested || generation != _openGeneration)
            return;
        if (NavigationHost.IsFocusActive)
            ApplyChangelogGamepadSelection(0);
        else
            ClearChangelogGamepadFocus();
    }

    public void Close()
    {
        ++_openGeneration;
        Model.Cancel();
        ClearChangelogGamepadFocus();
        FocusIndex = -1;
        IsVisible = false;
    }

    public DocumentView()
    {
        InitializeComponent();
        DataContext = Model;
        Model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DocumentViewModel.Content))
                RenderContent();
        };
    }

    public void ConfigureRenderer(MarkdownRenderer renderer) => _renderer = renderer;
    private void RenderContent()
    {
        if (Model.Content.IsMarkdown)
            ChangelogContent.ItemsSource = _renderer?.Render(Model.Content.Text, Model.Content.ImageBaseUrl);
        else
            ChangelogContent.ItemsSource = new Control[]
            {
                new TextBlock
                {
                    Text = Model.Content.Text,
                    Foreground = new SolidColorBrush(Color.Parse("#B8B8B8")),
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap,
                }
            };
    }

    private void CloseChangelog_Click(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke();
    private static void ClearStyledControlsGamepadFocusClasses(IReadOnlyList<Control> controls)
    {
        foreach (var control in controls)
            control.Classes.Remove("gamepad-focused");
    }

    public bool HandleChangelogGamepadNavigation(Services.NavigationDirection direction)
    {
        // Keep focus trapped on Close; Up/Down scroll the changelog body.
        var scrollViewer = this.FindControl<ScrollViewer>("ChangelogScrollViewer");
        if (scrollViewer != null)
        {
            const double step = 96;
            if (direction is Services.NavigationDirection.Down or Services.NavigationDirection.Right)
            {
                scrollViewer.Offset = new Avalonia.Vector(scrollViewer.Offset.X, scrollViewer.Offset.Y + step);
            }
            else if (direction is Services.NavigationDirection.Up or Services.NavigationDirection.Left)
            {
                scrollViewer.Offset = new Avalonia.Vector(scrollViewer.Offset.X, Math.Max(0, scrollViewer.Offset.Y - step));
            }
        }

        ApplyChangelogGamepadSelection(0);
        return true;
    }

    public List<Control> CollectChangelogFocusableControls()
    {
        var controls = new List<Control>();
        var closeButton = this.FindControl<Button>("CloseChangelogButton");
        if (closeButton != null && closeButton.IsVisible && closeButton.IsEnabled)
            controls.Add(closeButton);
        return controls;
    }

    public void ApplyChangelogGamepadSelection(int index)
    {
        var controls = CollectChangelogFocusableControls();
        index = NavigationHost.Navigation.ClampIndex(index, controls.Count);
        FocusIndex = index;
        NavigationHost.Navigation.ActiveZone = GamepadNavigationZone.ChangelogOverlay;
        NavigationHost.ClearFocus();
        NavigationHost.ClearSidebarFocus();
        ClearChangelogGamepadFocus();
        if (index < 0 || index >= controls.Count)
            return;
        if (controls[index] is StyledElement styled)
            styled.Classes.Set("gamepad-focused", true);
        controls[index].Focus();
    }

    public void ActivateChangelogGamepadSelection()
    {
        var controls = CollectChangelogFocusableControls();
        var index = NavigationHost.Navigation.ClampIndex(FocusIndex, controls.Count);
        if (index < 0 || index >= controls.Count)
            return;
        if (controls[index] is Button button)
            GamepadControlActivation.ActivateButton(button);
    }

    public void ClearChangelogGamepadFocus()
    {
        ClearStyledControlsGamepadFocusClasses(CollectChangelogFocusableControls());
    }

    public bool SynchronizePointer(object? source) => GamepadPointerFocusSync.Hit(NavigationHost.Navigation, CollectChangelogFocusableControls(), GamepadNavigationZone.ChangelogOverlay, FocusIndex, ApplyChangelogGamepadSelection, source);
}
