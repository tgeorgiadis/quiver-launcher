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
    private Action _save = null!;
    public event Action? AddRequested;
    public event Action? SearchChanged;
    public event Action? PreferredSizeChanged;
    public event EventHandler<RoutedEventArgs>? SearchLostFocus;
    public bool HasQuery => !string.IsNullOrWhiteSpace(LibrarySearchTextBox.Text);
    public bool SearchContainsFocus => LibrarySearchTextBox.IsFocused || LibrarySearchClearButton.IsFocused;
    public bool IsEditingSearch => GamepadTextInput.IsEditing && ReferenceEquals(GamepadTextInput.Active, LibrarySearchTextBox);
    public double PreferredWidth => 220 + AddNewEntryButton.DesiredSize.Width + SortByComboBox.DesiredSize.Width + 24;

    public LibraryToolbarView()
    {
        InitializeComponent();
        GamepadComboBoxNavigation.Attach(SortByComboBox);
        LibrarySearchTextBox.LostFocus += (sender, e) => SearchLostFocus?.Invoke(sender, e);
    }

    public void Configure(LibraryViewModel model, LauncherSession session, Action save)
    {
        _model = model;
        _session = session;
        _save = save;
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
        AddNewEntryButton,
        SortByComboBox
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

    public void SelectSort(string? tag)
    {
        foreach (var item in SortByComboBox.Items.OfType<ComboBoxItem>())
            if (item.Tag as string == tag)
            {
                SortByComboBox.SelectedItem = item;
                return;
            }
    }

    public void SortFlyoutOpening(MenuFlyout? flyout)
    {
        GamepadMenuFlyoutNavigation.Attach(flyout);
        if (flyout == null)
            return;
        var selected = (SortByComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        foreach (var item in flyout.Items.OfType<MenuItem>())
            item.FontWeight = item.Tag as string == selected ? FontWeight.Bold : FontWeight.Normal;
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

    private void SortByComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        PreferredSizeChanged?.Invoke();
        if (_session == null || _session.IsClosed || e.AddedItems.Count == 0)
            return;
        if (SortByComboBox.SelectedItem is not ComboBoxItem { Tag: string sort })
            return;
        _model.SortBy = sort;
        _model.Settings.Current.SortBy = sort;
        _save();
        _model.ApplySorting();
    }
}
