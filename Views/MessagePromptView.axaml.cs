using NavigationDirection = QuiverLauncher.Services.NavigationDirection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;
public partial class MessagePromptView : UserControl, IFeatureNavigationHandler
{
    public MessagePromptViewModel Model { get; } = new();

    private LauncherSession _session = null!;
    private int _index;
    private Control? _previousFocus;
    public MessagePromptView()
    {
        InitializeComponent();
        DataContext = Model;
        Model.Opened += FocusPrompt;
    }

    public void Configure(LauncherSession session) => _session = session;
    private void FocusPrompt(bool preferCancel)
    {
        _previousFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        var target = !Model.IsQuestion ? MessagePromptOkButton : preferCancel ? MessagePromptNoButton : MessagePromptYesButton;
        _index = Controls().IndexOf(target);
        target.Focus();
    }

    public async Task<MessagePromptResult> ShowAsync(string message, string title, bool isQuestion, bool preferCancelDefault = false, bool includeCancel = false)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(() => ShowAsync(message, title, isQuestion, preferCancelDefault, includeCancel));
        return await Model.ShowAsync(message, title, isQuestion, preferCancelDefault, includeCancel, _session.Token);
    }

    private List<Control> Controls() => new Control[]
    {
        MessagePromptYesButton,
        MessagePromptNoButton,
        MessagePromptCancelButton,
        MessagePromptOkButton
    }.Where(c => c.IsVisible).ToList();
    public void Complete(MessagePromptResult result)
    {
        Model.Complete(result);
        var previous = _previousFocus;
        _previousFocus = null;
        if (!_session.IsClosed && previous?.IsEffectivelyVisible == true && previous.IsEnabled && TopLevel.GetTopLevel(previous) != null)
            previous.Focus();
    }

    public bool Dismiss()
    {
        if (!Model.IsOpen)
            return false;
        Complete(Model.IncludeCancel ? MessagePromptResult.Cancel : Model.IsQuestion ? MessagePromptResult.No : MessagePromptResult.Yes);
        return true;
    }

    public bool Navigate(NavigationDirection direction)
    {
        if (!Model.IsOpen)
            return false;
        var controls = Controls();
        var focused = controls.FindIndex(c => c.IsFocused);
        if (focused >= 0)
            _index = focused;
        var delta = direction is NavigationDirection.Left or NavigationDirection.Up ? -1 : 1;
        _index = Math.Clamp(_index + delta, 0, controls.Count - 1);
        controls[_index].Focus();
        return true;
    }

    public bool Confirm()
    {
        if (!Model.IsOpen)
            return false;
        var controls = Controls();
        var button = controls.OfType<Button>().FirstOrDefault(c => c.IsFocused) ?? (Button)controls[Math.Clamp(_index, 0, controls.Count - 1)];
        GamepadControlActivation.ActivateButton(button);
        return true;
    }

    public bool Cancel() => Dismiss();
    public bool Options() => Model.IsOpen;
    public void RestoreFocus()
    {
        var controls = Controls();
        if (Model.IsOpen && controls.Count > 0)
            controls[Math.Clamp(_index, 0, controls.Count - 1)].Focus();
    }

    private void MessagePromptYesButton_Click(object? sender, RoutedEventArgs e) => Complete(MessagePromptResult.Yes);
    private void MessagePromptNoButton_Click(object? sender, RoutedEventArgs e) => Complete(MessagePromptResult.No);
    private void MessagePromptCancelButton_Click(object? sender, RoutedEventArgs e) => Complete(MessagePromptResult.Cancel);
    private void MessagePromptOkButton_Click(object? sender, RoutedEventArgs e) => Complete(MessagePromptResult.Yes);
    private void MessagePromptDimmer_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Dismiss();
        e.Handled = true;
    }
}
