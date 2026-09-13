using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class AppEntryEditorTests
{
    private sealed class SaveOperation : IAppEntryService
    {
        public AppEntryDraft? Draft { get; private set; }
        public int Calls { get; private set; }
        public TaskCompletionSource<bool> Completion { get; } = new();
        public Task<bool> SaveAsync(AppEntryDraft draft, Action<EntryNotice> notice, Action<GameInfo> openFolder, CancellationToken token)
        {
            Draft = draft;
            Calls++;
            return Completion.Task;
        }
    }

    [Fact]
    public async Task Save_captures_original_entry_identity_and_cannot_close_a_reopened_editor()
    {
        var operation = new SaveOperation();
        var model = new AppEntryEditorViewModel();
        model.Configure(operation);
        var game = new GameInfo { Name = "Old name", Repository = "owner/app", FolderName = "OldFolder" };
        model.Open(game);
        model.Name = "Updated name";
        model.FolderName = "NewFolder";
        var saving = model.SaveAsync(TestContext.Current.CancellationToken);
        (await model.SaveAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
        operation.Calls.Should().Be(1);
        operation.Draft!.EditingIdentityKey.Should().Be(game.InstanceKey);
        operation.Draft.FolderName.Should().Be("NewFolder");
        model.Close();
        model.Open();
        model.Name = "Another app";
        operation.Completion.SetResult(true);
        (await saving).Should().BeFalse();
        model.Name.Should().Be("Another app");
        model.Title.Should().Be("Create New Entry");
    }

    [Fact]
    public async Task Manual_entry_requires_name_and_folder_but_not_repository()
    {
        var operation = new SaveOperation();
        var model = new AppEntryEditorViewModel();
        model.Configure(operation);
        model.Open();
        model.ManuallyManaged = true;
        model.Name = "Manual app";
        (await model.SaveAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
        model.Validation.Should().Be("Error: Folder name is required");
        operation.Calls.Should().Be(0);
        model.FolderName = "ManualApp";
        operation.Completion.SetResult(true);
        (await model.SaveAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        operation.Draft!.ManuallyManaged.Should().BeTrue();
    }

    [AvaloniaFact]
    public void Entry_form_binds_edit_fields_and_manual_repository_visibility()
    {
        var view = new AppEntryEditorView();
        view.Model.Open(new GameInfo { Name = "Example", Repository = "owner/example", FolderName = "Example" });
        Dispatcher.UIThread.RunJobs();
        view.FindControl<TextBox>("NewGameNameTextBox")!.Text.Should().Be("Example");
        view.FindControl<Button>("CreateEditButton")!.Content.Should().Be("Update Entry");
        view.FindControl<CheckBox>("NewGameManuallyManagedCheckBox")!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        view.Model.ManuallyManaged.Should().BeTrue();
        view.FindControl<TextBox>("NewGameRepoTextBox")!.IsVisible.Should().BeFalse();
        view.FindControl<TextBox>("NewGameFolderTextBox")!.Text = "NewFolder";
        view.Model.FolderName.Should().Be("NewFolder");
    }
}
