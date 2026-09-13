using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class DisplayFilterEditorTests
{
    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new();
        public bool FailSave { get; set; }
        public int Saves { get; private set; }
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { if (FailSave) throw new IOException("Unavailable"); Saves++; }
    }
    [Fact]
    public void Editor_preserves_identity_and_exclude_rules_when_editing()
    {
        var store = new Store();
        var model = new DisplayFilterEditorViewModel(new SettingsViewModel(store));
        model.Open(null).Should().BeTrue();
        model.Name = "Favorites";
        model.Tags = "n64, favorites";
        model.ExcludeTags = "hidden";
        model.MatchModeIndex = 1;
        model.Save().Saved.Should().BeTrue();
        var filter = store.Current.TagDisplayFilters.Single();
        filter.MatchMode.Should().Be(TagFilterMatchMode.All);
        filter.Tags.Should().BeEquivalentTo(["n64", "favorites"]);
        model.Close();
        model.Open(filter.Id).Should().BeTrue();
        model.Name = "Nintendo";
        model.ExcludeMatchModeIndex = 1;
        var result = model.Save();
        result.WasEdit.Should().BeTrue();
        store.Current.TagDisplayFilters.Single().Should().BeSameAs(filter);
        filter.Name.Should().Be("Nintendo");
        filter.ExcludeTags.Should().Equal("hidden");
        filter.ExcludeMatchMode.Should().Be(TagFilterMatchMode.All);
    }
    [Fact]
    public void Invalid_closed_and_failed_saves_do_not_leave_partial_filters()
    {
        var store = new Store();
        var model = new DisplayFilterEditorViewModel(new SettingsViewModel(store));
        model.Open(null);
        model.Save().Error.Should().NotBeNull();
        model.Name = "Favorites";
        model.Save().Error.Should().NotBeNull();
        model.Tags = "favorite";
        store.FailSave = true;
        model.Invoking(m => m.Save()).Should().Throw<IOException>();
        store.Current.TagDisplayFilters.Should().BeEmpty();
        store.FailSave = false;
        model.Save().Saved.Should().BeTrue();
        store.Current.TagDisplayFilters.Should().ContainSingle();
        model.Close();
        model.Save().Saved.Should().BeFalse();
        model.Open(null);
        model.Name = "FAVORITES";
        model.ExcludeTags = "hidden";
        model.Save().ErrorTitle.Should().Be("Duplicate Filter");
        store.Saves.Should().Be(1);
    }
    [AvaloniaFact]
    public void Extracted_form_binds_fields_and_match_mode_help()
    {
        var store = new Store();
        var model = new DisplayFilterEditorViewModel(new SettingsViewModel(store));
        var view = new DisplayFilterEditorView { DataContext = model };
        model.Open(null);
        view.FindControl<TextBox>("DisplayFilterNameTextBox")!.Text = "Favorites";
        model.Name.Should().Be("Favorites");
        view.FindControl<ComboBox>("DisplayFilterMatchModeComboBox")!.SelectedIndex = 1;
        model.MatchModeIndex.Should().Be(1);
        view.FindControl<TextBlock>("DisplayFilterMatchModeHelpText")!.Text.Should().Be(model.MatchModeHelp);
    }
}
