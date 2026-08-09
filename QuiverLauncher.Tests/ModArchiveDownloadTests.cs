using System.Net;
using FluentAssertions;
using QuiverLauncher.Services.Mods;

namespace QuiverLauncher.Tests;

public class ModArchiveDownloadTests
{
    [Fact]
    public async Task DownloadToTempFileAsync_returns_file_stream_with_content()
    {
        var payload = Enumerable.Range(0, 100_000).Select(i => (byte)(i % 256)).ToArray();
        using var handler = new FixedBinaryHandler(payload);
        using var client = new HttpClient(handler);
        const string url = "https://example.com/large.mod";

        await using var stream = await ModArchiveDownload.DownloadToTempFileAsync(client, url);

        stream.Should().BeOfType<FileStream>();
        stream.CanSeek.Should().BeTrue();
        stream.Length.Should().Be(payload.Length);

        var buffer = new byte[payload.Length];
        var read = await stream.ReadAsync(buffer);
        read.Should().Be(payload.Length);
        buffer.Should().Equal(payload);
    }

    [Fact]
    public async Task DownloadToTempFileAsync_deletes_temp_file_on_dispose()
    {
        var payload = "hello-mod-archive"u8.ToArray();
        using var handler = new FixedBinaryHandler(payload);
        using var client = new HttpClient(handler);

        string? tempPath;
        {
            var stream = await ModArchiveDownload.DownloadToTempFileAsync(
                client,
                "https://example.com/mod.zip");
            tempPath = ((FileStream)stream).Name;
            File.Exists(tempPath).Should().BeTrue();
            await stream.DisposeAsync();
        }

        File.Exists(tempPath).Should().BeFalse("DeleteOnClose should remove the temp download");
    }

    [Fact]
    public async Task DownloadToTempFileAsync_reports_progress_to_completion()
    {
        var payload = new byte[50_000];
        Random.Shared.NextBytes(payload);
        using var handler = new FixedBinaryHandler(payload);
        using var client = new HttpClient(handler);

        var reports = new List<double>();
        var progress = new Progress<double>(reports.Add);

        await using var stream = await ModArchiveDownload.DownloadToTempFileAsync(
            client,
            "https://example.com/mod.bin",
            totalHint: 0,
            progress);

        // Progress<T> marshals asynchronously; flush queued callbacks.
        await Task.Yield();
        await Task.Delay(50);

        reports.Should().NotBeEmpty();
        reports.Last().Should().Be(1.0);
        reports.Should().OnlyContain(p => p >= 0 && p <= 1);
        stream.Length.Should().Be(payload.Length);
    }

    [Fact]
    public async Task CopyContentToTempFileAsync_cleans_up_partial_file_on_failure()
    {
        var content = new StreamContent(new FailAfterBytesStream(new byte[16_384]));
        content.Headers.ContentLength = 32_768;
        var tempDir = Path.Combine(Path.GetTempPath(), "QuiverMods");
        var before = Directory.Exists(tempDir)
            ? Directory.GetFiles(tempDir, "*.mod")
            : [];

        var act = async () => await ModArchiveDownload.CopyContentToTempFileAsync(content, totalHint: 32_768);
        await act.Should().ThrowAsync<IOException>();

        var after = Directory.Exists(tempDir)
            ? Directory.GetFiles(tempDir, "*.mod")
            : [];
        after.Should().BeEquivalentTo(before, "failed downloads should not leave .mod temp files");
    }

    private sealed class FixedBinaryHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;

        public FixedBinaryHandler(byte[] payload) => _payload = payload;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent(_payload);
            content.Headers.ContentLength = _payload.Length;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class FailAfterBytesStream : Stream
    {
        private readonly byte[] _bytes;
        private int _position;

        public FailAfterBytesStream(byte[] bytes) => _bytes = bytes;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _bytes.Length)
                throw new IOException("Simulated download failure");

            var toCopy = Math.Min(count, _bytes.Length - _position);
            Buffer.BlockCopy(_bytes, _position, buffer, offset, toCopy);
            _position += toCopy;
            return toCopy;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
