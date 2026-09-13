using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class CatalogIncrementalUpdateTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Compatibility_results_update_labels_and_bulk_actions_without_moving_cards(bool grid)
    {
        var view = new CatalogReviewView();
        var model = view.Model;
        var source = new AppCatalogSource { CachedListVersion = "1" };
        var apps = Enumerable.Range(0, 4).Select(i => new GameInfo
        {
            Name = "New app " + i, Repository = $"compat-{Guid.NewGuid():N}/app", FolderName = "New" + i
        }).ToList();
        model.PlatformFilters = ["Windows"];
        model.Refresh(source, [], apps);
        model.AcceptPlatformDiscoveries();
        model.Rows.UpdateWith(model.GetFilteredRows().ToList());
        var list = new ListBox
        {
            ItemsSource = model.Rows,
            ItemTemplate = (IDataTemplate)view.Resources[grid ? "CatalogReviewGridCardTemplate" : "CatalogReviewCardTemplate"]!,
            ItemsPanel = new FuncTemplate<Panel>(() => grid ? new CatalogReviewVirtualizingGridPanel() : new VirtualizingStackPanel())
        };
        view.Content = list;
        var window = new Window { Width = 900, Height = 700, Content = view };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var row = model.Rows[0];
            var container = list.ContainerFromIndex(0)!;
            var button = container.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible);
            button.Focus().Should().BeTrue();
            row.IsGamepadFocused = true;
            list.SelectedItem = row;
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
            var offset = scroll.Offset;
            var position = container.Bounds;
            var changes = 0;
            model.Rows.CollectionChanged += (_, _) => changes++;
            foreach (var app in apps) QuiverLauncher.Core.Services.CatalogPlatformIndex.Set("github", app.Repository, null, null,
                new() { tag_name = "v1", assets = [new() { name = app == apps[0] ? "app-Linux.AppImage" : "app-Windows.zip" }] });
            model.DeferPlatformDiscoveries();
            model.SetPlatformCheck(new(4, 4, CatalogReleaseWarmupOutcome.Completed));
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            changes.Should().Be(0);
            list.ContainerFromIndex(0).Should().BeSameAs(container);
            container.Bounds.Should().Be(position);
            list.SelectedItem.Should().BeSameAs(row);
            button.IsFocused.Should().BeTrue();
            row.IsGamepadFocused.Should().BeTrue();
            scroll.Offset.Should().Be(offset);
            container.GetVisualDescendants().OfType<TextBlock>().Should().Contain(t => t.Text == "Not available for Windows");
            model.FilteredBulkAddCount.Should().Be(3);
            model.ShowPlatformCheck.Should().BeFalse();
            model.AcceptPlatformDiscoveries();
            model.GetFilteredRows().Should().HaveCount(3);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(true, CatalogReviewFilter.All)]
    [InlineData(true, CatalogReviewFilter.NotInLibrary)]
    [InlineData(false, CatalogReviewFilter.All)]
    [InlineData(false, CatalogReviewFilter.NotInLibrary)]
    public void Adding_one_app_preserves_other_cards_and_focus(bool grid, CatalogReviewFilter filter)
    {
        var view = new CatalogReviewView();
        var model = view.Model;
        var source = new AppCatalogSource { Id = "incremental", CachedListVersion = "1" };
        var apps = Enumerable.Range(0, 100).Select(i => new GameInfo
        {
            Name = $"App {i:D3}", FolderName = "App" + i, Repository = "example/app" + i,
            Tags = ["games"],
        }).ToList();
        foreach (var app in apps) QuiverLauncher.Core.Services.CatalogPlatformIndex.Set("github", app.Repository, null, null, new() { tag_name = "v1", assets = [new() { name = "app-Windows.zip" }] });
        model.ReviewFilter = filter;
        model.Refresh(source, [], apps);
        void ShowRows()
        {
            var rows = model.GetFilteredRows().ToList();
            foreach (var row in rows) CatalogCompareService.ApplyReviewActionButtons(row, source, filter);
            model.Rows.UpdateWith(rows);
        }
        ShowRows();
        var list = new ListBox
        {
            ItemsSource = model.Rows,
            ItemTemplate = (IDataTemplate)view.Resources[grid ? "CatalogReviewGridCardTemplate" : "CatalogReviewCardTemplate"]!,
            ItemsPanel = new FuncTemplate<Panel>(() => grid ? new CatalogReviewVirtualizingGridPanel() : new VirtualizingStackPanel()),
        };
        view.Content = list;
        var window = new Window { Width = 1200, Height = 800, Content = view };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
            scroll.Offset = new Avalonia.Vector(0, 1000);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var unaffectedIndex = Enumerable.Range(2, 98).First(i => list.ContainerFromIndex(i) is { IsVisible: true } candidate &&
                candidate.TranslatePoint(default, list) is { Y: > 100 and < 450 });
            var addedIndex = unaffectedIndex - 1;
            var unaffected = model.Rows[unaffectedIndex];
            unaffected.IsGamepadFocused = true;
            var container = list.ContainerFromIndex(unaffectedIndex)!;
            container.Should().NotBeNull();
            var button = container.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible);
            button.Focus().Should().BeTrue();
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var offset = scroll.Offset;
            var tag = model.TagChips[0];
            var changes = new List<NotifyCollectionChangedAction>();
            model.Rows.CollectionChanged += (_, e) => changes.Add(e.Action);

            // Mirrors the post-save refresh, including newly deserialized catalog definitions.
            var local = CatalogCompareService.ApplyRowAdd([], model.AllRows[addedIndex]);
            model.Refresh(source, local, apps.Select(a => CatalogCompareService.CloneForLocal(a)).ToList());
            ShowRows();
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();

            changes.Should().NotContain(NotifyCollectionChangedAction.Reset);
            model.TagChips[0].Should().BeSameAs(tag);
            model.AllRows[unaffectedIndex].Should().BeSameAs(unaffected);
            list.ContainerFromIndex(model.Rows.IndexOf(unaffected)).Should().BeSameAs(container);
            button.IsFocused.Should().BeTrue();
            unaffected.IsGamepadFocused.Should().BeTrue();
            scroll.Offset.Should().Be(offset);
            model.NeedsReviewCount.Should().Be(99);
            model.FilteredBulkAddCount.Should().Be(99);
            model.Rows.Count.Should().Be(filter == CatalogReviewFilter.All ? 100 : 99);
            model.AllRows[addedIndex].CanAdd.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    [Fact]
    public void Changed_metadata_and_source_switches_are_not_mistaken_for_unchanged_rows()
    {
        var model = new CatalogSyncViewModel();
        var source = new AppCatalogSource { Id = "test" };
        var app = new GameInfo { Name = "App", Repository = "owner/app", FolderName = "App", PreferredVersion = "v1" };
        model.Refresh(source, [], [app]);
        var before = model.AllRows[0];
        app.PreferredVersion = "v2";
        model.Refresh(source, [], [app]);
        model.AllRows[0].Should().NotBeSameAs(before);
        before = model.AllRows[0];
        model.Refresh(new() { Id = "other" }, [], [app]);
        model.AllRows[0].Should().NotBeSameAs(before);
    }
}
