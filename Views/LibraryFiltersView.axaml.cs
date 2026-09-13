using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;
public partial class LibraryFiltersView : UserControl
{
    private LauncherSession _session = null!;
    private Func<string, string, Task> _message = null!;
    public LibraryFiltersViewModel Model { get; private set; } = null!;
    private AppSettings _settings => Model.Settings;
    private System.Collections.ObjectModel.ObservableCollection<TagDisplayFilterListItem> TagDisplayFilters => Model.TagDisplayFilters;

    public event Action<string?>? EditRequested;
    public event Action<string>? FocusRestorationRequested;
    public LibraryFiltersView()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => EndTagFilterDragSession();
    }

    public void Configure(LibraryFiltersViewModel model, LauncherSession session, Func<string, string, Task> message)
    {
        Model = model;
        DataContext = model;
        _session = session;
        _message = message;
    }

    public void ApplyMobileLayout()
    {
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        Surface.RowDefinitions[1].Height = GridLength.Auto;
        LibraryFiltersScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
    }

    public IReadOnlyList<Control> CollectNavigationControls()
    {
        var controls = new List<Control>();
        if (!IsVisible)
            return controls;
        controls.AddRange(new Control[] { UnhideAllGamesButton, HideNonInstalledButton, ShowHiddenGamesButton }.Where(c => c.IsVisible && c.IsEnabled));
        controls.AddRange(TagDisplayFiltersItemsControl.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("display-filter-row") && b.IsVisible && b.IsEnabled));
        var add = Surface.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsVisible && b.IsEnabled && b.Content is string text && string.Equals(text, "Add Display Filter", StringComparison.Ordinal));
        if (add != null)
            controls.Add(add);
        return controls;
    }

    private void ShowDisplayFilterOverlay(string? filterId) => EditRequested?.Invoke(filterId);
    private void RestoreSidebarDisplayFilterFocus(string filterId) => FocusRestorationRequested?.Invoke(filterId);
    private void OnSettingChanged() => RunAction(Model.Save, "Failed to save settings");
    private void RunAction(Action action, string error)
    {
        if (_session.IsClosed)
            return;
        try
        {
            action();
            RefreshSidebarFilterSelection();
        }
        catch (Exception ex)
        {
            _ = _session.RunAsync(() => _message($"{error}: {ex.Message}", "Error"));
        }
    }

    private string? _tagFilterDragId;
    private double _tagFilterDragStartY;
    private double _tagFilterDragListTop;
    private double _tagFilterDragRowStride;
    private bool _tagFilterDragActive;
    private bool _tagFilterSuppressRowClick;
    private List<string>? _tagFilterOrderAtDragStart;
    private Button? _tagFilterDragRowButton;
    private IPointer? _tagFilterDragPointer;
    internal void RefreshSidebarFilterSelection()
    {
        _settings.EnsureInitialized();
        UnhideAllGamesButton?.Classes.Set("selected", _settings.ListScope == AppListScope.AllApps);
        HideNonInstalledButton?.Classes.Set("selected", _settings.ListScope == AppListScope.InstalledOnly);
        ShowHiddenGamesButton?.Classes.Set("selected", _settings.ListScope == AppListScope.HiddenOnly);
    }

    private void AddDisplayFilter_Click(object? sender, RoutedEventArgs e)
    {
        ShowDisplayFilterOverlay(null);
    }

    private void TagDisplayFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_tagFilterSuppressRowClick)
        {
            _tagFilterSuppressRowClick = false;
            return;
        }

        if (sender is not Button button || button.Tag is not string filterId)
            return;
        ToggleTagDisplayFilter(filterId);
    }

    private void TagDisplayFilterReorder_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border handle || handle.Tag is not string filterId)
            return;
        if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
            return;
        if (TagDisplayFiltersItemsControl == null)
            return;
        _tagFilterSuppressRowClick = true;
        _tagFilterDragId = filterId;
        _tagFilterDragStartY = e.GetPosition(TagDisplayFiltersItemsControl).Y;
        _tagFilterDragActive = false;
        _tagFilterDragPointer = e.Pointer;
        _tagFilterDragRowButton = handle.GetVisualAncestors().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("display-filter-row"));
        if (_tagFilterDragRowButton != null)
            BeginTagDisplayFilterDragVisuals(_tagFilterDragRowButton);
        TagDisplayFiltersItemsControl.AddHandler(InputElement.PointerMovedEvent, TagDisplayFilterDragPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        TagDisplayFiltersItemsControl.AddHandler(InputElement.PointerReleasedEvent, TagDisplayFilterDragPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        e.Pointer.Capture(TagDisplayFiltersItemsControl);
        e.Handled = true;
    }

    private void TagDisplayFilterDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_tagFilterDragId == null || TagDisplayFiltersItemsControl == null)
            return;
        if (!e.GetCurrentPoint(TagDisplayFiltersItemsControl).Properties.IsLeftButtonPressed)
            return;
        var currentY = e.GetPosition(TagDisplayFiltersItemsControl).Y;
        if (!_tagFilterDragActive && Math.Abs(currentY - _tagFilterDragStartY) >= 3)
        {
            _tagFilterDragActive = true;
            _tagFilterOrderAtDragStart = _settings.TagDisplayFilters.Select(f => f.Id).ToList();
            if (TryMeasureTagFilterDragMetrics(_tagFilterDragId, out var listTop, out var rowStride))
            {
                _tagFilterDragListTop = listTop;
                _tagFilterDragRowStride = rowStride;
            }

            ReapplyTagDisplayFilterDragVisuals();
            _tagFilterDragPointer?.Capture(TagDisplayFiltersItemsControl);
        }

        if (!_tagFilterDragActive)
            return;
        var currentIndex = GetTagDisplayFilterIndex(_tagFilterDragId);
        if (currentIndex < 0 || _tagFilterDragRowStride <= 0)
            return;
        var targetIndex = TagDisplayFilterDragDrop.ResolveInsertIndex(TagDisplayFilters.Count, currentIndex, currentY, _tagFilterDragListTop, _tagFilterDragRowStride);
        if (targetIndex != currentIndex)
            PreviewMoveTagDisplayFilter(_tagFilterDragId, targetIndex);
        e.Handled = true;
    }

    private void TagDisplayFilterDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_tagFilterDragId == null)
            return;
        var didDrag = _tagFilterDragActive;
        var orderAtStart = _tagFilterOrderAtDragStart;
        EndTagFilterDragSession();
        if (e.Pointer.Captured != null)
            e.Pointer.Capture(null);
        if (didDrag)
        {
            if (orderAtStart != null && !orderAtStart.SequenceEqual(_settings.TagDisplayFilters.Select(f => f.Id)))
            {
                OnSettingChanged();
            }
        }
        else
        {
            _tagFilterSuppressRowClick = true;
        }

        e.Handled = true;
    }

    private void BeginTagDisplayFilterDragVisuals(Button row)
    {
        row.Classes.Add("dragging");
        row.ZIndex = 10;
        _tagFilterDragRowButton = row;
        SetTagDisplayFilterTooltipsEnabled(false);
    }

    private void SetTagDisplayFilterTooltipsEnabled(bool enabled)
    {
        if (TagDisplayFiltersItemsControl == null)
            return;
        foreach (var row in TagDisplayFiltersItemsControl.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("display-filter-row")))
        {
            ToolTip.SetServiceEnabled(row, enabled);
            if (!enabled)
                ToolTip.SetIsOpen(row, false);
        }
    }

    private void ReapplyTagDisplayFilterDragVisuals()
    {
        if (_tagFilterDragId == null)
            return;
        var row = GetTagDisplayFilterRow(_tagFilterDragId);
        if (row == null)
            return;
        row.Classes.Add("dragging");
        row.ZIndex = 10;
        _tagFilterDragRowButton = row;
    }

    private Button? GetTagDisplayFilterRow(string filterId)
    {
        if (TagDisplayFiltersItemsControl == null)
            return null;
        return TagDisplayFiltersItemsControl.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("display-filter-row") && b.Tag is string id && string.Equals(id, filterId, StringComparison.OrdinalIgnoreCase));
    }

    private void ClearTagDisplayFilterDragVisuals()
    {
        SetTagDisplayFilterTooltipsEnabled(true);
        if (_tagFilterDragId != null)
        {
            var row = GetTagDisplayFilterRow(_tagFilterDragId);
            if (row != null)
            {
                row.Classes.Remove("dragging");
                row.ZIndex = 0;
            }
        }

        _tagFilterDragRowButton?.Classes.Remove("dragging");
    }

    internal void EndTagFilterDragSession()
    {
        if (ReferenceEquals(_tagFilterDragPointer?.Captured, TagDisplayFiltersItemsControl))
            _tagFilterDragPointer?.Capture(null);
        if (TagDisplayFiltersItemsControl != null)
        {
            TagDisplayFiltersItemsControl.RemoveHandler(InputElement.PointerMovedEvent, TagDisplayFilterDragPointerMoved);
            TagDisplayFiltersItemsControl.RemoveHandler(InputElement.PointerReleasedEvent, TagDisplayFilterDragPointerReleased);
        }

        ClearTagDisplayFilterDragVisuals();
        _tagFilterDragId = null;
        _tagFilterDragActive = false;
        _tagFilterDragRowButton = null;
        _tagFilterDragPointer = null;
        _tagFilterOrderAtDragStart = null;
        _tagFilterDragListTop = 0;
        _tagFilterDragRowStride = 0;
        _tagFilterSuppressRowClick = false;
    }

    private bool TryMeasureTagFilterDragMetrics(string filterId, out double listTop, out double rowStride)
    {
        listTop = 0;
        rowStride = 0;
        if (TagDisplayFiltersItemsControl == null)
            return false;
        var row = GetTagDisplayFilterRow(filterId);
        if (row == null)
            return false;
        var transform = row.TransformToVisual(TagDisplayFiltersItemsControl);
        if (transform == null)
            return false;
        var rowHeight = row.Bounds.Height;
        if (rowHeight <= 0)
            return false;
        rowStride = rowHeight + 4;
        var dragIndex = GetTagDisplayFilterIndex(filterId);
        if (dragIndex < 0)
            return false;
        var rowTop = transform.Value.Transform(new Point(0, 0)).Y;
        listTop = rowTop - dragIndex * rowStride;
        return true;
    }

    private void DisplayFilterOverflow_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button button)
            OpenDisplayFilterOverflowMenu(button);
    }

    internal void OpenDisplayFilterOverflowMenu(Button overflowButton)
    {
        if (overflowButton.ContextMenu == null)
            return;
        UpdateDisplayFilterOverflowMenuState(overflowButton);
        overflowButton.ContextMenu.PlacementTarget = overflowButton;
        overflowButton.ContextMenu.Placement = PlacementMode.Bottom;
        GamepadContextMenuNavigation.Attach(overflowButton.ContextMenu);
        overflowButton.ContextMenu.Open();
    }

    private void UpdateDisplayFilterOverflowMenuState(Button overflowButton)
    {
        if (overflowButton.ContextMenu == null)
            return;
        _settings.EnsureInitialized();
        var filterId = overflowButton.Tag as string;
        var index = string.IsNullOrEmpty(filterId) ? -1 : GetTagDisplayFilterIndex(filterId);
        var count = _settings.TagDisplayFilters.Count;
        foreach (var item in overflowButton.ContextMenu.Items.OfType<MenuItem>())
        {
            if (item.Header is not string header)
                continue;
            if (string.Equals(header, "Move Up", StringComparison.Ordinal))
                item.IsEnabled = index > 0;
            else if (string.Equals(header, "Move Down", StringComparison.Ordinal))
                item.IsEnabled = index >= 0 && index < count - 1;
        }
    }

    internal static Button? FindDisplayFilterOverflowButton(Button row) => row.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("display-filter-overflow") && b.ContextMenu != null);
    private static string? GetFilterIdFromSender(object? sender) => sender switch
    {
        Button { Tag: string id } => id,
        MenuItem { Tag: string id } => id,
        _ => null
    };
    private void MoveTagDisplayFilterUp_Click(object? sender, RoutedEventArgs e) => MoveTagDisplayFilterByOffset(GetFilterIdFromSender(sender), -1);
    private void MoveTagDisplayFilterDown_Click(object? sender, RoutedEventArgs e) => MoveTagDisplayFilterByOffset(GetFilterIdFromSender(sender), 1);
    private void MoveTagDisplayFilterByOffset(string? filterId, int offset)
    {
        if (string.IsNullOrEmpty(filterId))
            return;
        _settings.EnsureInitialized();
        var fromIndex = GetTagDisplayFilterIndex(filterId);
        if (fromIndex < 0)
            return;
        var toIndex = fromIndex + offset;
        if (toIndex < 0 || toIndex >= _settings.TagDisplayFilters.Count)
            return;
        // Close the overflow menu before the list moves. Destroying its host
        // can skip Closed and leave gamepad/keyboard input trapped on the menu.
        GamepadContextMenuNavigation.Instance.TryHandleOptionsDismiss();
        PreviewMoveTagDisplayFilter(filterId, toIndex);
        OnSettingChanged();
        RestoreSidebarDisplayFilterFocus(filterId);
    }

    private void EditTagDisplayFilter_Click(object? sender, RoutedEventArgs e)
    {
        var filterId = GetFilterIdFromSender(sender);
        if (filterId == null)
            return;
        ShowDisplayFilterOverlay(filterId);
    }

    private void UnhideAllGamesButton_Click(object? sender, RoutedEventArgs e) => RunAction(() => Model.SetScope(AppListScope.AllApps), "Failed to show all apps");
    private void HideNonInstalledButton_Click(object? sender, RoutedEventArgs e) => RunAction(() => Model.SetScope(AppListScope.InstalledOnly), "Failed to filter installed apps");
    private void ShowHiddenGamesButton_Click(object? sender, RoutedEventArgs e) => RunAction(() => Model.SetScope(AppListScope.HiddenOnly), "Failed to show hidden apps");
    private void ToggleTagDisplayFilter(string filterId)
    {
        RunAction(() => Model.Toggle(filterId), "Failed to filter apps");
        RestoreSidebarDisplayFilterFocus(filterId);
    }

    private int GetTagDisplayFilterIndex(string? filterId) => Model.IndexOf(filterId);
    private void PreviewMoveTagDisplayFilter(string filterId, int targetIndex)
    {
        Model.PreviewMove(filterId, targetIndex);
        _tagFilterDragRowButton = GetTagDisplayFilterRow(filterId);
    }

    private void DeleteTagDisplayFilter_Click(object? sender, RoutedEventArgs e)
    {
        if (GetFilterIdFromSender(sender)is not string filterId)
            return;
        GamepadContextMenuNavigation.Instance.TryHandleOptionsDismiss();
        RunAction(() => Model.Delete(filterId), "Failed to delete filter");
    }
}
