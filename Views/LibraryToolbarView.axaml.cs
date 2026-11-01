using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;
public partial class LibraryToolbarView : UserControl
{
    private LauncherSession? _session;
    private LibraryViewModel _model = null!;
    public event Action? AddRequested;
    public event Action? SearchChanged;
    public event EventHandler<RoutedEventArgs>? SearchLostFocus;
    public bool HasQuery => !string.IsNullOrWhiteSpace(LibrarySearchTextBox.Text);
    public bool SearchContainsFocus => LibrarySearchTextBox.IsFocused || LibrarySearchClearButton.IsFocused;
    public bool IsEditingSearch => GamepadTextInput.IsEditing && ReferenceEquals(GamepadTextInput.Active, LibrarySearchTextBox);
    public double PreferredWidth => 320 + AddNewEntryButton.DesiredSize.Width + 12;

    public LibraryToolbarView()
    {
        InitializeComponent();
        LibrarySearchTextBox.LostFocus += (sender, e) => SearchLostFocus?.Invoke(sender, e);
    }

    public void Configure(LibraryViewModel model, LauncherSession session)
    {
        _model = model;
        _session = session;
        DataContext = model;
    }

    public void ClearSearch() => LibrarySearchTextBox.Text = "";
    public void FocusSearch() => LibrarySearchTextBox.Focus();
    public void SetSearchVisible(bool visible) => LibrarySearchHost.IsVisible = visible;
    public void RefreshClearButton() => LibrarySearchClearButton.IsVisible = HasQuery;
    public void MoveSearchTo(Panel destination)
    {
        if (ReferenceEquals(LibrarySearchHost.Parent, destination))
            return;
        if (LibrarySearchHost.Parent is Panel parent)
            parent.Children.Remove(LibrarySearchHost);
        destination.Children.Add(LibrarySearchHost);
        Grid.SetColumn(LibrarySearchHost, 0);
        LibrarySearchHost.Margin = new Thickness(0);
        LibrarySearchHost.MinWidth = 0;
        LibrarySearchHost.Width = double.NaN;
        LibrarySearchHost.MaxWidth = double.PositiveInfinity;
        LibrarySearchHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        LibrarySearchHost.VerticalAlignment = VerticalAlignment.Center;
        LibrarySearchHost.IsVisible = false;
    }

    public IReadOnlyList<Control> NavigationControls(bool searchOnly = false) => (searchOnly ? new Control[]
    {
        LibrarySearchTextBox,
        LibrarySearchClearButton
    }

    : new Control[]
    {
        LibrarySearchTextBox,
        LibrarySearchClearButton,
        AddNewEntryButton
    }

    ).Where(c => c.IsEffectivelyVisible && c.IsEnabled).ToArray();
    public int SearchIndex(IReadOnlyList<Control> controls)
    {
        for (var i = 0; i < controls.Count; i++)
            if (ReferenceEquals(controls[i], LibrarySearchTextBox))
                return i;
        return -1;
    }

    public bool ShouldRestoreSearchFocus(GamepadNavigationService navigation, IReadOnlyList<Control> controls)
    {
        if (ReferenceEquals(GamepadTextInput.Active, LibrarySearchTextBox))
            return true;
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (focused is Visual visual && (ReferenceEquals(visual, LibrarySearchTextBox) || LibrarySearchTextBox.IsVisualAncestorOf(visual)))
            return true;
        var index = navigation.ClampIndex(navigation.TopBarSelectedIndex, controls.Count);
        return navigation.ActiveZone == GamepadNavigationZone.TopBar && index >= 0 && index == SearchIndex(controls);
    }

    private void AddNewEntryButton_Click(object? sender, RoutedEventArgs e) => AddRequested?.Invoke();
    private void LibrarySearchClear_Click(object? sender, RoutedEventArgs e) => ClearSearch();
    private async void LibrarySearch_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_session == null)
            return;
        await _session.RunAsync(async () =>
        {
            if (await _model.SearchAsync(LibrarySearchTextBox.Text ?? "", _session.Token))
            {
                RefreshClearButton();
                SearchChanged?.Invoke();
            }
        });
    }
}
