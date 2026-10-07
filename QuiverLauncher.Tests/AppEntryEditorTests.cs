using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class AppEntryEditorTests
{
    [Fact]
    public async Task Save_stays_busy_until_the_library_refresh_finishes()
    {
        var operation = new SaveOperation();
        operation.Completion.SetResult(true);
        var model = new AppEntryEditorViewModel();
        model.Configure(operation);
        model.Open();
        model.Name = "jakdexter";
        model.FolderName = "jakanddexter";
        model.Repository = "https://github.com/open-goal/launcher";
        var refreshed = new TaskCompletionSource();
        var saving = model.SaveAsync(TestContext.Current.CancellationToken, () => refreshed.Task);
        try
        {
            model.CanSave.Should().BeFalse();
            (await model.SaveAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
            operation.Calls.Should().Be(1);
        }
        finally { refreshed.TrySetResult(); }
        (await saving).Should().BeTrue();
        model.CanSave.Should().BeTrue();
    }

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
}
