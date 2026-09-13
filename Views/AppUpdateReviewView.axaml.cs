using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;
public partial class AppUpdateReviewView : UserControl, IFeatureNavigationHandler
{
    public bool Navigate(Services.NavigationDirection direction) => HandleAppUpdatesReviewGamepadNavigation(direction);
    public bool Confirm()
    {
        switch (NavigationHost.Navigation.ActiveZone)
        {
            case GamepadNavigationZone.AppUpdatesReviewToolbar:
                ActivateAppUpdatesReviewToolbarSelection();
                break;
            case GamepadNavigationZone.AppUpdatesReviewRowActions:
                ActivateAppUpdatesReviewRowActionSelection();
                break;
            default:
                ActivateAppUpdatesReviewRowSelection();
                break;
        }

        return true;
    }

    public bool Cancel()
    {
        if (NavigationHost.Navigation.ActiveZone == GamepadNavigationZone.AppUpdatesReviewList)
            Model.Back();
        else
        {
            ClearAppUpdatesReviewToolbarGamepadFocus();
            ClearAppUpdatesReviewRowActionsGamepadFocus();
            SelectInitialAppUpdatesReviewGamepadItem();
        }

        return true;
    }

    public bool Options() => false;
    public void RestoreFocus() => SelectInitialAppUpdatesReviewGamepadItem();
    public AppUpdateReviewViewModel Model { get; } = new();
    public IFeatureNavigationHost NavigationHost { get; set; } = null!;
    public Func<bool> IsActive { get; set; } = () => false;

    public AppUpdateReviewView()
    {
        InitializeComponent();
        DataContext = Model;
    }

    public Button? FindActionButton(GameInfo game, string content) => AppUpdatesReviewItemsControl.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => ReferenceEquals(button.DataContext, game) && string.Equals(button.Content?.ToString(), content, StringComparison.Ordinal));
    public Control GetUpdateAnchor(GameInfo game) => FindActionButton(game, "Update") ?? (Control)AppUpdatesUpdateAllButton;
    public Control GetVersionsAnchor(GameInfo game) => FindActionButton(game, "Versions") ?? (Control)this;
    private void AppUpdatesBackToLibrary_Click(object? sender, RoutedEventArgs e) => Model.Back();
    private async void AppUpdatesUpdateAll_Click(object? sender, RoutedEventArgs e) => await Model.UpdateAllAsync();
    private async void AppUpdatesSkipAll_Click(object? sender, RoutedEventArgs e) => await Model.SkipAllAsync();
    private async void AppUpdateReviewRowUpdate_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: GameInfo game } button)
            return;
        button.IsEnabled = false;
        try
        {
            await Model.UpdateAsync(game);
        }
        finally
        {
            if (IsActive())
                button.IsEnabled = true;
        }
    }

    private async void AppUpdateReviewRowSkip_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: GameInfo game })
            await Model.SkipAsync(game);
    }

    private void AppUpdateReviewRowVersions_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: GameInfo game })
            Model.ShowVersions(game);
    }

    private static void ClearStyledControlsGamepadFocusClasses(IReadOnlyList<Control> controls)
    {
        foreach (var control in controls)
            control.Classes.Remove("gamepad-focused");
    }

    private void ClearFocusIfOnControls(IReadOnlyList<Control> controls)
    {
        var manager = TopLevel.GetTopLevel(this)?.FocusManager;
        if (manager?.GetFocusedElement()is Control focused && controls.Contains(focused))
            manager.Focus(null);
    }

    private bool TryMoveXyFocusInRegion(Control? root, Services.NavigationDirection direction, IReadOnlyList<Control> controls, Action<int> apply)
    {
        var previous = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        var previousIndex = GamepadControlActivation.IndexOfControlContainingFocus(controls, previous);
        if (!XyFocusNavigation.TryMove(this, direction, root))
            return false;
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        var index = GamepadControlActivation.IndexOfControlContainingFocus(controls, focused);
        if (index < 0 || index == previousIndex)
            return false;
        apply(index);
        return true;
    }

    public bool HandleAppUpdatesReviewGamepadNavigation(Services.NavigationDirection direction)
    {
        if (NavigationHost.Navigation.ActiveZone == GamepadNavigationZone.AppUpdatesReviewToolbar)
            return HandleAppUpdatesReviewToolbarNavigation(direction);
        if (NavigationHost.Navigation.ActiveZone == GamepadNavigationZone.AppUpdatesReviewRowActions)
            return HandleAppUpdatesReviewRowActionsNavigation(direction);
        var rows = Model.Rows.ToList();
        var currentIndex = NavigationHost.Navigation.AppUpdatesReviewSelectedIndex;
        var zoneTransition = NavigationHost.Navigation.TryGetZoneTransition(direction, GamepadNavigationZone.AppUpdatesReviewList, NavigationHost.MainContentZone, isListLayout: true, positions: null, currentIndex, rows.Count);
        if (zoneTransition.HasValue)
            return NavigationHost.ApplyTransition(zoneTransition.Value);
        if (rows.Count == 0)
            return true;
        if (direction == Services.NavigationDirection.Right)
        {
            ApplyAppUpdatesReviewRowActionSelection(0);
            return true;
        }

        var nextIndex = NavigationHost.Navigation.MoveListIndex(currentIndex, direction, rows.Count, wrap: false);
        ApplyAppUpdatesReviewRowSelection(nextIndex);
        return true;
    }

    public bool HandleAppUpdatesReviewToolbarNavigation(Services.NavigationDirection direction)
    {
        var controls = CollectAppUpdatesReviewToolbarControls();
        if (controls.Count == 0)
            return false;
        var currentIndex = NavigationHost.Navigation.AppUpdatesReviewToolbarIndex;
        var zoneTransition = NavigationHost.Navigation.TryGetZoneTransition(direction, GamepadNavigationZone.AppUpdatesReviewToolbar, NavigationHost.MainContentZone, isListLayout: true, positions: null, currentIndex, controls.Count);
        if (zoneTransition.HasValue)
            return NavigationHost.ApplyTransition(zoneTransition.Value);
        if (direction is not (Services.NavigationDirection.Left or Services.NavigationDirection.Right))
            return true;
        if (direction == Services.NavigationDirection.Left && currentIndex <= 0)
        {
            return NavigationHost.ApplyTransition(new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null));
        }

        if (TryMoveXyFocusInRegion(this, direction, controls, ApplyAppUpdatesReviewToolbarSelection))
            return true;
        var nextIndex = NavigationHost.Navigation.MoveHorizontalIndex(currentIndex, direction, controls.Count);
        ApplyAppUpdatesReviewToolbarSelection(nextIndex);
        return true;
    }

    public bool HandleAppUpdatesReviewRowActionsNavigation(Services.NavigationDirection direction)
    {
        var rows = Model.Rows.ToList();
        if (rows.Count == 0)
            return false;
        var rowIndex = NavigationHost.Navigation.ClampIndex(NavigationHost.Navigation.AppUpdatesReviewSelectedIndex, rows.Count);
        if (rowIndex < 0 || rowIndex >= rows.Count)
            return false;
        var controls = CollectAppUpdatesReviewRowActionControls(rows[rowIndex]);
        if (controls.Count == 0)
            return false;
        var currentIndex = NavigationHost.Navigation.AppUpdatesReviewRowActionIndex;
        if (direction is Services.NavigationDirection.Up or Services.NavigationDirection.Down)
        {
            ClearAppUpdatesReviewRowActionsGamepadFocus();
            var nextRow = NavigationHost.Navigation.MoveListIndex(rowIndex, direction, rows.Count, wrap: false);
            ApplyAppUpdatesReviewRowSelection(nextRow);
            return true;
        }

        if (direction == Services.NavigationDirection.Left && currentIndex <= 0)
        {
            ClearAppUpdatesReviewRowActionsGamepadFocus();
            ApplyAppUpdatesReviewRowSelection(rowIndex);
            return true;
        }

        if (direction is not (Services.NavigationDirection.Left or Services.NavigationDirection.Right))
            return false;
        var nextIndex = NavigationHost.Navigation.MoveHorizontalIndex(currentIndex, direction, controls.Count);
        ApplyAppUpdatesReviewRowActionSelection(nextIndex);
        return true;
    }

    public List<Control> CollectAppUpdatesReviewToolbarControls()
    {
        var controls = new List<Control>();
        void Add(Control? control)
        {
            if (control != null && control.IsVisible && control.IsEnabled)
                controls.Add(control);
        }

        Add(this.FindControl<Button>("AppUpdatesUpdateAllButton"));
        Add(this.FindControl<Button>("AppUpdatesSkipAllButton"));
        Add(this.FindControl<Button>("AppUpdatesBackToLibraryButton"));
        return controls;
    }

    public List<Control> CollectAppUpdatesReviewRowActionControls(GameInfo game)
    {
        var controls = new List<Control>();
        var itemsControl = this.FindControl<ItemsControl>("AppUpdatesReviewItemsControl");
        if (itemsControl == null)
            return controls;
        foreach (var button in itemsControl.GetVisualDescendants().OfType<Button>())
        {
            if (!ReferenceEquals(button.DataContext, game))
                continue;
            if (!button.IsVisible || !button.IsEnabled)
                continue;
            controls.Add(button);
        }

        return controls;
    }

    public void ApplyAppUpdatesReviewToolbarSelection(int index)
    {
        var controls = CollectAppUpdatesReviewToolbarControls();
        index = NavigationHost.Navigation.ClampIndex(index, controls.Count);
        NavigationHost.Navigation.AppUpdatesReviewToolbarIndex = index;
        NavigationHost.Navigation.ActiveZone = GamepadNavigationZone.AppUpdatesReviewToolbar;
        NavigationHost.ClearFocus();
        ClearAppUpdatesReviewRowFocus();
        ClearStyledControlsGamepadFocusClasses(controls);
        if (index < 0 || index >= controls.Count)
            return;
        if (controls[index] is StyledElement styled)
            styled.Classes.Set("gamepad-focused", true);
        controls[index].Focus();
    }

    public void ApplyAppUpdatesReviewRowSelection(int index, bool stealFocus = true)
    {
        var rows = Model.Rows.ToList();
        index = NavigationHost.Navigation.ClampIndex(index, rows.Count);
        NavigationHost.Navigation.AppUpdatesReviewSelectedIndex = index;
        NavigationHost.Navigation.AppUpdatesReviewRowActionIndex = -1;
        NavigationHost.Navigation.ActiveZone = GamepadNavigationZone.AppUpdatesReviewList;
        ClearAppUpdatesReviewToolbarGamepadFocus();
        ClearAppUpdatesReviewRowActionsGamepadFocus();
        ClearAppUpdatesReviewRowFocus();
        NavigationHost.ClearFocus();
        if (index < 0 || index >= rows.Count)
            return;
        NavigationHost.FocusCard(stealFocus);
        rows[index].IsGamepadFocused = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (IsActive() && IsVisible)
                FindAppUpdateReviewRowBorder(rows[index])?.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    public void ApplyAppUpdatesReviewRowActionSelection(int index)
    {
        var rows = Model.Rows.ToList();
        var rowIndex = NavigationHost.Navigation.ClampIndex(NavigationHost.Navigation.AppUpdatesReviewSelectedIndex, rows.Count);
        if (rowIndex < 0 || rowIndex >= rows.Count)
            return;
        var controls = CollectAppUpdatesReviewRowActionControls(rows[rowIndex]);
        index = NavigationHost.Navigation.ClampIndex(index, controls.Count);
        NavigationHost.Navigation.AppUpdatesReviewRowActionIndex = index;
        NavigationHost.Navigation.ActiveZone = GamepadNavigationZone.AppUpdatesReviewRowActions;
        ClearAppUpdatesReviewToolbarGamepadFocus();
        ClearStyledControlsGamepadFocusClasses(controls);
        if (index < 0 || index >= controls.Count)
            return;
        if (controls[index] is StyledElement styled)
            styled.Classes.Set("gamepad-focused", true);
        controls[index].Focus();
    }

    public void ClearAppUpdatesReviewToolbarGamepadFocus()
    {
        var controls = CollectAppUpdatesReviewToolbarControls();
        ClearStyledControlsGamepadFocusClasses(controls);
        ClearFocusIfOnControls(controls);
    }

    public void ClearAppUpdatesReviewRowActionsGamepadFocus()
    {
        var rows = Model.Rows.ToList();
        var rowIndex = NavigationHost.Navigation.ClampIndex(NavigationHost.Navigation.AppUpdatesReviewSelectedIndex, rows.Count);
        if (rowIndex < 0 || rowIndex >= rows.Count)
            return;
        ClearStyledControlsGamepadFocusClasses(CollectAppUpdatesReviewRowActionControls(rows[rowIndex]));
    }

    public void ClearAppUpdatesReviewRowFocus()
    {
        foreach (var game in Model.Rows)
            game.IsGamepadFocused = false;
    }

    public Border? FindAppUpdateReviewRowBorder(GameInfo game)
    {
        var itemsControl = this.FindControl<ItemsControl>("AppUpdatesReviewItemsControl");
        return itemsControl?.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => ReferenceEquals(b.DataContext, game));
    }

    public void SelectInitialAppUpdatesReviewGamepadItem()
    {
        if (!NavigationHost.IsFocusActive)
        {
            NavigationHost.ClearFocus();
            return;
        }

        if (Model.Rows.Count == 0)
        {
            ApplyAppUpdatesReviewToolbarSelection(0);
            return;
        }

        ApplyAppUpdatesReviewRowSelection(0);
    }

    public void ActivateAppUpdatesReviewToolbarSelection()
    {
        var controls = CollectAppUpdatesReviewToolbarControls();
        var index = NavigationHost.Navigation.ClampIndex(NavigationHost.Navigation.AppUpdatesReviewToolbarIndex, controls.Count);
        if (index < 0 || index >= controls.Count)
            return;
        if (controls[index] is Button button)
            GamepadControlActivation.ActivateButton(button);
    }

    public void ActivateAppUpdatesReviewRowSelection()
    {
        var rows = Model.Rows.ToList();
        var index = NavigationHost.Navigation.ClampIndex(NavigationHost.Navigation.AppUpdatesReviewSelectedIndex, rows.Count);
        if (index < 0 || index >= rows.Count)
            return;
        var controls = CollectAppUpdatesReviewRowActionControls(rows[index]);
        if (controls.Count == 0)
            return;
        ApplyAppUpdatesReviewRowActionSelection(0);
    }

    public void ActivateAppUpdatesReviewRowActionSelection()
    {
        var rows = Model.Rows.ToList();
        var rowIndex = NavigationHost.Navigation.ClampIndex(NavigationHost.Navigation.AppUpdatesReviewSelectedIndex, rows.Count);
        if (rowIndex < 0 || rowIndex >= rows.Count)
            return;
        var controls = CollectAppUpdatesReviewRowActionControls(rows[rowIndex]);
        var index = NavigationHost.Navigation.ClampIndex(NavigationHost.Navigation.AppUpdatesReviewRowActionIndex, controls.Count);
        if (index < 0 || index >= controls.Count)
            return;
        if (controls[index] is Button button)
            GamepadControlActivation.ActivateButton(button);
    }

    public bool SynchronizePointer(object? source)
    {
        if (GamepadPointerFocusSync.Hit(NavigationHost.Navigation, CollectAppUpdatesReviewToolbarControls(), GamepadNavigationZone.AppUpdatesReviewToolbar, NavigationHost.Navigation.AppUpdatesReviewToolbarIndex, ApplyAppUpdatesReviewToolbarSelection, source))
        {
            return true;
        }

        var rows = Model.Rows.ToList();
        var rowIndex = GamepadPointerFocusSync.IndexOfDataContext(rows, source as Visual);
        if (rowIndex >= 0)
        {
            var actions = CollectAppUpdatesReviewRowActionControls(rows[rowIndex]);
            var actionIndex = GamepadControlActivation.IndexOfControlContainingFocus(actions, source);
            if (actionIndex >= 0)
            {
                if (NavigationHost.Navigation.ActiveZone == GamepadNavigationZone.AppUpdatesReviewRowActions && NavigationHost.Navigation.AppUpdatesReviewSelectedIndex == rowIndex && NavigationHost.Navigation.AppUpdatesReviewRowActionIndex == actionIndex)
                {
                    return true;
                }

                GamepadTextInput.Reset();
                NavigationHost.Navigation.AppUpdatesReviewSelectedIndex = rowIndex;
                ApplyAppUpdatesReviewRowActionSelection(actionIndex);
                return true;
            }

            return GamepadPointerFocusSync.Card(NavigationHost.Navigation, rows, GamepadNavigationZone.AppUpdatesReviewList, NavigationHost.Navigation.AppUpdatesReviewSelectedIndex, index => ApplyAppUpdatesReviewRowSelection(index, stealFocus: false), source);
        }

        return false;
    }

    public void LeaveZone(GamepadNavigationZone nextZone)
    {
        if (NavigationHost.Navigation.ActiveZone == GamepadNavigationZone.AppUpdatesReviewToolbar && nextZone != GamepadNavigationZone.AppUpdatesReviewToolbar)
            ClearAppUpdatesReviewToolbarGamepadFocus();
    }

    public bool EnterZone(GamepadZoneTransition transition)
    {
        switch (transition.Zone)
        {
            case GamepadNavigationZone.AppUpdatesReviewToolbar:
                ApplyAppUpdatesReviewToolbarSelection(transition.SelectedIndex ?? 0);
                return true;
            case GamepadNavigationZone.AppUpdatesReviewList:
                if (Model.Rows.Count == 0)
                {
                    ApplyAppUpdatesReviewToolbarSelection(0);
                    return true;
                }

                ApplyAppUpdatesReviewRowSelection(transition.SelectedIndex ?? 0);
                return true;
            case GamepadNavigationZone.AppUpdatesReviewRowActions:
                ApplyAppUpdatesReviewRowActionSelection(transition.SelectedIndex ?? (NavigationHost.Navigation.AppUpdatesReviewRowActionIndex < 0 ? 0 : NavigationHost.Navigation.AppUpdatesReviewRowActionIndex));
                return true;
            default:
                return false;
        }
    }
}
