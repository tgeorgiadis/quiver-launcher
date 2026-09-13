using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Platform;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.Services.Mods;
using QuiverLauncher.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Avalonia.Platform;
using AsyncImageLoader;

namespace QuiverLauncher.Views;
/// <summary>Focus traversal and activation within the settings surface.</summary>
public sealed class SettingsNavigationController : IFeatureNavigationHandler
{
    public bool Navigate(Services.NavigationDirection direction) => HandleSettingsGamepadNavigation(direction);
    public bool Confirm()
    {
        ActivateSettingsGamepadSelection();
        return true;
    }

    public bool Cancel()
    {
        _view.RequestClose();
        return true;
    }

    public bool Options() => false;
    public void RestoreFocus() => ApplySettingsGamepadSelection(Math.Max(0, FocusIndex));
    private readonly SettingsView _view;
    private readonly GamepadNavigationService _navigation;
    public SettingsNavigationController(SettingsView view, GamepadNavigationService navigation)
    {
        _view = view;
        _navigation = navigation;
    }

    public int FocusIndex { get; set; } = -1;

    public bool HandleSettingsGamepadNavigation(Services.NavigationDirection direction)
    {
        var controls = CollectSettingsFocusableControls();
        if (controls.Count == 0)
            return true;
        var tabs = CollectSettingsTabItems();
        var focused = GetSettingsFocusedControl(controls);
        var closeIndex = FindSettingsCloseButtonIndex(controls);
        // Tab strip: Left/Right switch tabs; Up goes to close; Down enters content.
        if (focused is TabItem)
        {
            if (direction is Services.NavigationDirection.Left or Services.NavigationDirection.Right)
            {
                NavigateSettingsTabHeader(direction);
                return true;
            }

            if (direction == Services.NavigationDirection.Up)
            {
                if (closeIndex >= 0)
                    ApplySettingsGamepadSelection(closeIndex);
                return true;
            }

            if (direction == Services.NavigationDirection.Down)
            {
                var contentIndex = FindFirstSettingsContentIndex(controls);
                if (contentIndex >= 0)
                    ApplySettingsGamepadSelection(contentIndex);
                return true;
            }

            return true;
        }

        // Close button sits above the tab strip.
        if (closeIndex >= 0 && FocusIndex == closeIndex)
        {
            if (direction is Services.NavigationDirection.Down or Services.NavigationDirection.Right)
            {
                ApplySettingsGamepadSelection(GetSelectedSettingsTabControlIndex(tabs));
                return true;
            }

            return true;
        }

        // Sliders: Left/Right change the value; Up/Down still move between controls.
        if (focused is Slider slider && direction is Services.NavigationDirection.Left or Services.NavigationDirection.Right)
        {
            AdjustSettingsSliderValue(slider, direction);
            return true;
        }

        if (TryMoveXyFocusInRegion(_view, direction, controls, ApplySettingsGamepadSelection))
            return true;
        var contentIndices = CollectSettingsContentIndices(controls);
        var contentPos = contentIndices.IndexOf(FocusIndex);
        if (contentPos >= 0)
        {
            var nextPos = FindNearestSettingsContentIndex(controls, contentIndices, contentPos, direction);
            if (nextPos >= 0)
            {
                ApplySettingsGamepadSelection(contentIndices[nextPos]);
                return true;
            }
        }

        if (direction == Services.NavigationDirection.Up)
            ApplySettingsGamepadSelection(GetSelectedSettingsTabControlIndex(tabs));
        return true;
    }

    public static void AdjustSettingsSliderValue(Slider slider, Services.NavigationDirection direction)
    {
        var step = slider.TickFrequency > 0 ? slider.TickFrequency : slider.SmallChange > 0 ? slider.SmallChange : 1;
        var delta = direction == Services.NavigationDirection.Right ? step : -step;
        var next = Math.Clamp(slider.Value + delta, slider.Minimum, slider.Maximum);
        if (slider.TickFrequency > 0)
        {
            var ticksFromMin = Math.Round((next - slider.Minimum) / slider.TickFrequency);
            next = Math.Clamp(slider.Minimum + (ticksFromMin * slider.TickFrequency), slider.Minimum, slider.Maximum);
        }

        slider.Value = next;
        // Card layout behind Settings can steal keyboard focus when sizes change;
        // keep the slider focused for continued Left/Right adjustment.
        slider.Focus();
    }

    public int FindNearestSettingsContentIndex(IReadOnlyList<Control> controls, IReadOnlyList<int> contentIndices, int contentPos, Services.NavigationDirection direction)
    {
        if (contentPos < 0 || contentPos >= contentIndices.Count)
            return -1;
        var currentCenter = GetSettingsControlCenter(controls[contentIndices[contentPos]]);
        if (!currentCenter.HasValue)
            return -1;
        int? bestPos = null;
        var bestScore = double.MaxValue;
        for (var i = 0; i < contentIndices.Count; i++)
        {
            if (i == contentPos)
                continue;
            var candidateCenter = GetSettingsControlCenter(controls[contentIndices[i]]);
            if (!candidateCenter.HasValue)
                continue;
            var score = ScoreSettingsNavigation(currentCenter.Value, candidateCenter.Value, direction);
            if (!score.HasValue || score.Value >= bestScore)
                continue;
            bestScore = score.Value;
            bestPos = i;
        }

        return bestPos ?? -1;
    }

    public Avalonia.Point? GetSettingsControlCenter(Control control)
    {
        var origin = _view as Visual ?? _view;
        var topLeft = control.TranslatePoint(new Avalonia.Point(0, 0), origin);
        if (!topLeft.HasValue)
            return null;
        var bounds = control.Bounds;
        return new Avalonia.Point(topLeft.Value.X + bounds.Width / 2, topLeft.Value.Y + bounds.Height / 2);
    }

    public static double? ScoreSettingsNavigation(Avalonia.Point current, Avalonia.Point candidate, Services.NavigationDirection direction)
    {
        var dx = candidate.X - current.X;
        var dy = candidate.Y - current.Y;
        // Up/Down must change rows so left-aligned checkboxes (Fill Cards) are not
        // skipped in favor of full-width sliders further down, and so preset-row
        // Left/Right neighbors are not treated as Up/Down targets.
        const double rowTolerance = 20;
        switch (direction)
        {
            case Services.NavigationDirection.Up:
                if (dy >= -rowTolerance)
                    return null;
                // Closest row below/above wins; light X tie-break only.
                return Math.Abs(dy) + (Math.Abs(dx) * 0.05);
            case Services.NavigationDirection.Down:
                if (dy <= rowTolerance)
                    return null;
                return Math.Abs(dy) + (Math.Abs(dx) * 0.05);
            case Services.NavigationDirection.Left:
                if (dx >= -1)
                    return null;
            {
                var secondary = Math.Abs(dy);
                var offAxis = secondary > 10 ? secondary * 2.5 : 0;
                return Math.Abs(dx) + (secondary * 0.3) + offAxis;
            }

            case Services.NavigationDirection.Right:
                if (dx <= 1)
                    return null;
            {
                var secondary = Math.Abs(dy);
                var offAxis = secondary > 10 ? secondary * 2.5 : 0;
                return Math.Abs(dx) + (secondary * 0.3) + offAxis;
            }

            default:
                return null;
        }
    }

    public void NavigateSettingsTabHeader(Services.NavigationDirection direction)
    {
        if (_view.SettingsTabControl == null)
            return;
        var tabs = CollectSettingsTabItems();
        if (tabs.Count == 0)
            return;
        var currentTabIndex = _view.SettingsTabControl.SelectedIndex;
        if (FocusIndex >= 0)
        {
            var controls = CollectSettingsFocusableControls();
            if (FocusIndex < controls.Count && controls[FocusIndex] is TabItem focusedTab)
            {
                currentTabIndex = tabs.IndexOf(focusedTab);
            }
        }

        if (currentTabIndex < 0)
            currentTabIndex = 0;
        var nextTabIndex = direction == Services.NavigationDirection.Left ? (currentTabIndex - 1 + tabs.Count) % tabs.Count : (currentTabIndex + 1) % tabs.Count;
        _view.SettingsTabControl.SelectedIndex = nextTabIndex;
        ApplySettingsGamepadSelection(nextTabIndex);
    }

    public int GetSelectedSettingsTabControlIndex(IReadOnlyList<TabItem> tabs)
    {
        if (tabs.Count == 0)
            return 0;
        var selected = _view.SettingsTabControl?.SelectedIndex ?? 0;
        return Math.Clamp(selected, 0, tabs.Count - 1);
    }

    public int FindSettingsCloseButtonIndex(IReadOnlyList<Control> controls)
    {
        if (_view.CloseSettingsButton == null)
            return -1;
        for (var i = 0; i < controls.Count; i++)
        {
            if (ReferenceEquals(controls[i], _view.CloseSettingsButton))
                return i;
        }

        return -1;
    }

    public int FindFirstSettingsContentIndex(IReadOnlyList<Control> controls)
    {
        for (var i = 0; i < controls.Count; i++)
        {
            if (controls[i] is TabItem)
                continue;
            if (_view.CloseSettingsButton != null && ReferenceEquals(controls[i], _view.CloseSettingsButton))
                continue;
            return i;
        }

        return -1;
    }

    public List<int> CollectSettingsContentIndices(IReadOnlyList<Control> controls)
    {
        var indices = new List<int>();
        for (var i = 0; i < controls.Count; i++)
        {
            if (controls[i] is TabItem)
                continue;
            if (_view.CloseSettingsButton != null && ReferenceEquals(controls[i], _view.CloseSettingsButton))
                continue;
            indices.Add(i);
        }

        return indices;
    }

    public List<TabItem> CollectSettingsTabItems()
    {
        if (_view.SettingsTabControl == null)
            return[];
        var tabs = new List<TabItem>();
        foreach (var item in _view.SettingsTabControl.Items)
        {
            if (item is not TabItem tab)
                continue;
            tab.Focusable = true;
            if (tab.IsVisible && tab.IsEnabled)
                tabs.Add(tab);
        }

        return tabs;
    }

    public List<Control> CollectSettingsFocusableControls()
    {
        if (_view == null)
            return[];
        var controls = new List<Control>();
        controls.AddRange(CollectSettingsTabItems());
        // Close sits outside tab content; include it explicitly.
        if (_view.CloseSettingsButton is { IsEffectivelyVisible: true, IsEnabled: true, Focusable: true })
            controls.Add(_view.CloseSettingsButton);
        // Only walk the selected tab page. Walking the whole _view also
        // picks up ScrollViewer/TabControl RepeatButtons above the first option,
        // which made Up from "Close After Launch" take two presses to reach tabs.
        var contentRoot = _view.SettingsTabControl?.SelectedItem is TabItem { Content: Control page } ? page : null;
        if (contentRoot == null)
            return controls;
        foreach (var control in contentRoot.GetVisualDescendants().OfType<Control>())
        {
            if (control is RepeatButton)
                continue;
            if (!control.IsEffectivelyVisible || !control.IsEnabled || !control.Focusable)
                continue;
            if (control is Button or CheckBox or TextBox or Slider or ComboBox)
                controls.Add(control);
        }

        return controls;
    }

    public Control? GetSettingsFocusedControl(IReadOnlyList<Control> controls)
    {
        var focused = TopLevel.GetTopLevel(_view)?.FocusManager?.GetFocusedElement();
        var actualIndex = GamepadControlActivation.IndexOfControlContainingFocus(controls, focused);
        if (actualIndex >= 0) FocusIndex = actualIndex;
        if (FocusIndex < 0 || FocusIndex >= controls.Count)
            return null;
        return controls[FocusIndex];
    }

    public void ApplySettingsGamepadSelection(int index)
    {
        var controls = CollectSettingsFocusableControls();
        index = _navigation.ClampIndex(index, controls.Count);
        FocusIndex = index;
        _navigation.ActiveZone = GamepadNavigationZone.Settings;
        ClearSettingsGamepadFocusClasses(controls);
        if (index < 0 || index >= controls.Count)
            return;
        if (controls[index] is StyledElement styled)
            styled.Classes.Set("gamepad-focused", true);
        GamepadControlActivation.ApplyGamepadHighlightFocus(controls[index]);
        Dispatcher.UIThread.Post(() =>
        {
            if (!_view.IsClosed && _view.IsVisible)
                controls[index].BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    public void ActivateSettingsGamepadSelection()
    {
        var controls = CollectSettingsFocusableControls();
        var focused = TopLevel.GetTopLevel(_view)?.FocusManager?.GetFocusedElement();
        var focusedIndex = GamepadControlActivation.IndexOfControlContainingFocus(controls, focused);
        var index = focusedIndex >= 0 ? focusedIndex : _navigation.ClampIndex(FocusIndex, controls.Count);
        if (index < 0 || index >= controls.Count)
            return;
        if (focusedIndex >= 0)
            FocusIndex = focusedIndex;
        var control = controls[index];
        if (control is TabItem tabItem)
        {
            var tabs = CollectSettingsTabItems();
            var tabIndex = tabs.IndexOf(tabItem);
            if (tabIndex >= 0 && _view.SettingsTabControl != null)
                _view.SettingsTabControl.SelectedIndex = tabIndex;
            // Move into the first content control below the tab strip.
            var refreshed = CollectSettingsFocusableControls();
            var firstContentIndex = FindFirstSettingsContentIndex(refreshed);
            if (firstContentIndex >= 0)
                ApplySettingsGamepadSelection(firstContentIndex);
            else
                ApplySettingsGamepadSelection(Math.Max(0, tabIndex));
            return;
        }

        if (control is CheckBox checkBox)
        {
            GamepadControlActivation.ActivateCheckBox(checkBox);
            // Layout-changing App Cards checkboxes rebuild the library underneath;
            // re-assert settings focus so keyboard focus does not land on a game card.
            ApplySettingsGamepadSelection(index);
        }
        else if (control is Button button)
        {
            GamepadControlActivation.ActivateButton(button);
            if (_view.IsVisible)
            {
                // Rebinding disables other buttons, changing their indices. Follow
                // the initiating control rather than clamping its old index to Reset.
                var currentIndex = CollectSettingsFocusableControls().IndexOf(button);
                if (currentIndex >= 0) ApplySettingsGamepadSelection(currentIndex);
            }
        }
        else if (control is ComboBox comboBox)
            GamepadComboBoxNavigation.Open(comboBox);
        else if (control is TextBox textBox)
            GamepadControlActivation.ActivateTextBox(textBox);
        else
            control.Focus();
    }

    public void ClearSettingsGamepadFocusClasses(IReadOnlyList<Control> controls)
    {
        foreach (var control in controls)
        {
            if (control is StyledElement styled)
                styled.Classes.Set("gamepad-focused", false);
        }
    }

    private bool TryMoveXyFocusInRegion(Control? root, Services.NavigationDirection direction, IReadOnlyList<Control> controls, Action<int> apply)
    {
        var previous = TopLevel.GetTopLevel(_view)?.FocusManager?.GetFocusedElement();
        var previousIndex = GamepadControlActivation.IndexOfControlContainingFocus(controls, previous);
        if (!XyFocusNavigation.TryMove(_view, direction, root))
            return false;
        var focused = TopLevel.GetTopLevel(_view)?.FocusManager?.GetFocusedElement();
        var index = GamepadControlActivation.IndexOfControlContainingFocus(controls, focused);
        if (index < 0 || index == previousIndex)
            return false;
        apply(index);
        return true;
    }

    public bool SynchronizePointer(object? source) => GamepadPointerFocusSync.Hit(_navigation, CollectSettingsFocusableControls(), GamepadNavigationZone.Settings, FocusIndex, ApplySettingsGamepadSelection, source);
}
