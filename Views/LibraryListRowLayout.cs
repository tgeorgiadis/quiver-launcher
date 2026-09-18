using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;

/// <summary>Fits list-only presentation into the chosen height without changing app state.</summary>
public sealed class LibraryListRowLayout : Panel
{
    public static readonly AttachedProperty<string> RoleProperty = AvaloniaProperty.RegisterAttached<LibraryListRowLayout, Control, string>("Role", "");
    public static readonly StyledProperty<SettingsViewModel?> SettingsProperty = AvaloniaProperty.Register<LibraryListRowLayout, SettingsViewModel?>(nameof(Settings));
    public static string GetRole(Control control) => control.GetValue(RoleProperty);
    public static void SetRole(Control control, string value) => control.SetValue(RoleProperty, value);
    public SettingsViewModel? Settings { get => GetValue(SettingsProperty); set => SetValue(SettingsProperty, value); }
    private readonly Dictionary<Control, Rect> _placements = new();
    private GameInfo? _game;
    private SettingsViewModel? _settings;
    private bool _attached;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Subscribe();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        Subscribe();
        base.OnDetachedFromVisualTree(e);
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DataContextProperty || change.Property == SettingsProperty) Subscribe();
    }
    private void Subscribe()
    {
        if (_game != null) _game.PropertyChanged -= Changed;
        if (_settings != null) _settings.PropertyChanged -= Changed;
        _game = _attached ? DataContext as GameInfo : null;
        _settings = _attached ? Settings : null;
        if (_game != null) _game.PropertyChanged += Changed;
        if (_settings != null) _settings.PropertyChanged += Changed;
        UpdateDescription();
        InvalidateMeasure();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        UpdateDescription();
        InvalidateMeasure();
    }
    private void UpdateDescription()
    {
        if (_game == null) return;
        var description = string.Join("\n", new[] { _game.DisplayName, _game.ProjectSubtitle, _game.StatusText,
            _game.ShowReleaseVersionInfo && !string.IsNullOrWhiteSpace(_game.LatestVersion) ? _game.LatestVersionLabel : "", _game.HasPreferredVersion ? _game.PreferredVersionLabel : "",
            string.Join(", ", _game.LibraryCardTags), _game.HasModUpdates ? "Mod updates available" : "",
            _game.HasRepositoryCheckError ? _game.RepositoryCheckError : "" }.Where(s => !string.IsNullOrWhiteSpace(s)));
        ToolTip.SetTip(this, description);
        AutomationProperties.SetHelpText(this, description);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 600;
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : Settings?.ListRowHeight ?? 96;
        Place(new Size(width, height));
        foreach (var child in Children)
            child.Measure(_placements.TryGetValue(child, out var rect) ? rect.Size : default);
        return new Size(width, height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        Place(finalSize);
        foreach (var child in Children)
            child.Arrange(_placements.TryGetValue(child, out var rect) ? rect : default);
        return finalSize;
    }

    private void Place(Size size)
    {
        _placements.Clear();
        var game = DataContext as GameInfo;
        var settings = Settings;
        var innerHeight = Math.Max(0, size.Height - 12);
        var actionSize = Math.Clamp(settings?.ActionButtonSize ?? 36, PlatformCapabilities.IsMobile ? 44 : 32, Math.Max(PlatformCapabilities.IsMobile ? 44 : 32, innerHeight));
        var primaryWidth = Math.Max(96, Math.Min(112, size.Width * .25));
        var actionsWidth = primaryWidth + 8 + actionSize;
        var imageSize = Math.Min(Math.Min(settings?.IconSize ?? 84, innerHeight), Math.Max(32, size.Width * .2));
        var textLeft = 12 + imageSize + 12;
        var textWidth = Math.Max(0, size.Width - textLeft - actionsWidth - 24);
        var y = 6.0;
        var bottom = size.Height - 6;


        void Put(string role, Rect rect, bool visible = true)
        {
            var child = Children.FirstOrDefault(c => GetRole(c) == role);
            if (child == null) return;
            child.IsVisible = visible;
            child.ClipToBounds = true;
            if (visible) _placements[child] = rect;
        }
        void Line(string role, double height, bool wanted)
        {
            var fits = wanted && y + height <= bottom + .1;
            Put(role, new Rect(textLeft, y, textWidth, height), fits);
            if (fits) y += height + 1;
        }
        Put("Thumbnail", new Rect(12, 6, imageSize, imageSize));
        Line("Title", 22, true);
        Line("Subtitle", 15, game?.HasProjectSubtitle == true);
        if (Children.FirstOrDefault(c => GetRole(c) == "Status") is HoverScrollText status)
            status.Text = game?.StatusText + (game?.HasModUpdates == true ? " · Mod updates available" : "");
        Line("Status", 15, true);
        Line("Latest", 16, game?.ShowReleaseVersionInfo == true && !string.IsNullOrWhiteSpace(game.LatestVersion) && game.LatestVersion != game.InstalledVersion);
        Line("Preferred", 16, game?.HasPreferredVersion == true);
        y += 4; // Separate tags from the version/status lines, including compact rows.
        var tagHeight = Math.Min(Math.Max(0, Math.Floor((bottom - y) / 18) * 18), (game?.LibraryCardTagMaxLines ?? 0) * 18);
        Put("Tags", new Rect(textLeft, y, textWidth, Math.Max(0, tagHeight)), game?.HasLibraryCardTags == true && tagHeight >= 18);
        Put("Actions", new Rect(size.Width - 12 - actionsWidth, (size.Height - actionSize) / 2, actionsWidth, actionSize));
        if (Children.FirstOrDefault(c => GetRole(c) == "Actions") is Panel actions)
        {
            var buttons = actions.Children.OfType<Button>().ToArray();
            for (var i = 0; i < buttons.Length; i++)
            {
                buttons[i].Height = actionSize;
                buttons[i].Width = i == 0 ? primaryWidth : actionSize;
                buttons[i].MinWidth = 0;
                buttons[i].MinHeight = 0;
            }
        }
        Put("Badge", new Rect(12, Math.Max(6, imageSize - 18), 24, 24), game?.ShowUpdateBadge == true);
        Put("Progress", new Rect(0, Math.Max(0, size.Height - 4), size.Width, 4), game?.IsDownloading == true);
    }
}
