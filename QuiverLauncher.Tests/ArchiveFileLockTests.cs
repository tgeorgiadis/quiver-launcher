using FluentAssertions;
using QuiverLauncher.Core.Services;

namespace QuiverLauncher.Tests;

public class ArchiveFileLockTests
{
    [Fact]
    public void IsSharingOrLockViolation_detects_sharing_hresult()
    {
        var ex = new IOException(
            "The process cannot access the file because it is being used by another process.",
            unchecked((int)0x80070020));

        ArchiveFileLock.IsSharingOrLockViolation(ex).Should().BeTrue();
    }

    [Fact]
    public void IsSharingOrLockViolation_detects_lock_hresult()
    {
        var ex = new IOException("The file is locked.", unchecked((int)0x80070021));

        ArchiveFileLock.IsSharingOrLockViolation(ex).Should().BeTrue();
    }

    [Fact]
    public void IsSharingOrLockViolation_ignores_other_io_errors()
    {
        var ex = new IOException("Disk full.", unchecked((int)0x80070070));

        ArchiveFileLock.IsSharingOrLockViolation(ex).Should().BeFalse();
    }

    [Fact]
    public void FormatMessage_includes_file_name_and_guidance()
    {
        var message = ArchiveFileLock.FormatMessage(@"C:\Temp\CutTheRopeDX-v2.29.0.3-Windows-x64.7z");

        message.Should().Contain("CutTheRopeDX-v2.29.0.3-Windows-x64.7z");
        message.Should().Contain("still locked by another process");
        message.Should().Contain("Windows Defender");
    }

    [Fact]
    public async Task OpenReadAsync_opens_with_readwrite_share()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);

        try
        {
            await using var other = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            await using var opened = await ArchiveFileLock.OpenReadAsync(path, maxAttempts: 2, delayAsync: _ => Task.CompletedTask);
            opened.Length.Should().Be(4);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenReadAsync_retries_then_throws_formatted_lock_error()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.bin");
        var delays = 0;
        await using var exclusive = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        exclusive.WriteByte(1);
        exclusive.Flush();

        try
        {
            var act = async () =>
            {
                await using var _ = await ArchiveFileLock.OpenReadAsync(
                    path,
                    maxAttempts: 3,
                    delayAsync: _ =>
                    {
                        delays++;
                        return Task.CompletedTask;
                    });
            };

            var error = await act.Should().ThrowAsync<IOException>();
            error.Which.Message.Should().Contain("still locked by another process");
            delays.Should().Be(2);
        }
        finally
        {
            await exclusive.DisposeAsync();
            File.Delete(path);
        }
    }
}

public class DownloadStagingTests
{
    [Fact]
    public void CreateStagedDownload_uses_unique_folders_and_keeps_asset_name()
    {
        var root = Path.Combine(Path.GetTempPath(), "QuiverStagingTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var (dir1, file1) = DownloadStaging.CreateStagedDownload(
                root,
                "CutTheRopeDX-v2.29.0.3-Windows-x64.7z");
            var (dir2, file2) = DownloadStaging.CreateStagedDownload(
                root,
                "CutTheRopeDX-v2.29.0.3-Windows-x64.7z");

            Path.GetFileName(file1).Should().Be("CutTheRopeDX-v2.29.0.3-Windows-x64.7z");
            Path.GetFileName(file2).Should().Be("CutTheRopeDX-v2.29.0.3-Windows-x64.7z");
            dir1.Should().NotBe(dir2);
            file1.Should().NotBe(file2);
            Directory.Exists(dir1).Should().BeTrue();
            Directory.Exists(dir2).Should().BeTrue();
            file1.Should().StartWith(dir1);
            file2.Should().StartWith(dir2);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryDeleteDirectory_removes_staging_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "QuiverStagingTest-" + Guid.NewGuid().ToString("N"));
        var (dir, file) = DownloadStaging.CreateStagedDownload(root, "payload.7z");
        File.WriteAllText(file, "temp");

        DownloadStaging.TryDeleteDirectory(dir);

        Directory.Exists(dir).Should().BeFalse();
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
