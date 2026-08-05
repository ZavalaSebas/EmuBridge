using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using EmuBridge.Models;
using EmuBridge.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmuBridge.Tests.Services;

public class DownloadVerificationServiceTests : IDisposable
{
    private readonly string _downloadDirectory;

    public DownloadVerificationServiceTests()
    {
        _downloadDirectory = Path.Combine(Path.GetTempPath(), $"emubridge_download_test_{Guid.NewGuid()}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_downloadDirectory))
        {
            Directory.Delete(_downloadDirectory, recursive: true);
        }
    }

    // "example.com" only — deliberately not the real Config.AllowedDownloadHosts, so these tests
    // never accidentally validate against (or drift from) the real production allow-list.
    private static readonly HashSet<string> TestAllowedHosts = new(StringComparer.OrdinalIgnoreCase) { "example.com" };

    private DownloadVerificationService CreateService(FakeHttpMessageHandler handler)
        => new(new HttpClient(handler), _downloadDirectory, TestAllowedHosts, NullLogger<DownloadVerificationService>.Instance);

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static HttpResponseMessage OkResponse(byte[] bytes, bool suppressContentLength = false)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        if (suppressContentLength)
        {
            response.Content.Headers.ContentLength = null;
        }

        return response;
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_ValidDownload_ReturnsSuccessAndLeavesOnlyFinalFile()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var handler = new FakeHttpMessageHandler(_ => OkResponse(bytes));
        var service = CreateService(handler);

        var result = await service.DownloadAndVerifyAsync("https://example.com/emu.7z", "emu.7z", Sha256Hex(bytes), bytes.Length);

        Assert.Equal(DownloadOutcome.Success, result.Outcome);
        Assert.NotNull(result.FilePath);
        Assert.True(File.Exists(result.FilePath));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(result.FilePath!));
        Assert.Single(Directory.GetFiles(_downloadDirectory));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_HashMismatch_DeletesFileAndReturnsSpecificMessage()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var handler = new FakeHttpMessageHandler(_ => OkResponse(bytes));
        var service = CreateService(handler);

        var result = await service.DownloadAndVerifyAsync("https://example.com/emu.7z", "emu.7z", new string('0', 64), bytes.Length);

        Assert.Equal(DownloadOutcome.HashMismatch, result.Outcome);
        Assert.Contains("didn't match what was expected", result.ErrorMessage);
        Assert.Empty(Directory.GetFiles(_downloadDirectory));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_ContentLengthLargerThanExpected_RejectsBeforeWritingAnyFile()
    {
        var bytes = new byte[1000];
        var handler = new FakeHttpMessageHandler(_ => OkResponse(bytes));
        var service = CreateService(handler);

        var result = await service.DownloadAndVerifyAsync("https://example.com/emu.7z", "emu.7z", Sha256Hex(bytes), 10);

        Assert.Equal(DownloadOutcome.SizeExceeded, result.Outcome);
        Assert.Empty(Directory.GetFiles(_downloadDirectory));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_NoContentLengthBodyExceedsExpected_CutsOffEarlyAndDeletesFile()
    {
        var bytes = new byte[1000];
        var handler = new FakeHttpMessageHandler(_ => OkResponse(bytes, suppressContentLength: true));
        var service = CreateService(handler);

        var result = await service.DownloadAndVerifyAsync("https://example.com/emu.7z", "emu.7z", Sha256Hex(bytes), 10);

        Assert.Equal(DownloadOutcome.SizeExceeded, result.Outcome);
        Assert.Contains("larger than expected", result.ErrorMessage);
        Assert.Empty(Directory.GetFiles(_downloadDirectory));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_NoContentLengthBodyShorterThanExpected_ReturnsSizeExceededAndDeletesFile()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var handler = new FakeHttpMessageHandler(_ => OkResponse(bytes, suppressContentLength: true));
        var service = CreateService(handler);

        var result = await service.DownloadAndVerifyAsync("https://example.com/emu.7z", "emu.7z", Sha256Hex(bytes), 1000);

        Assert.Equal(DownloadOutcome.SizeExceeded, result.Outcome);
        Assert.Contains("did not complete as expected", result.ErrorMessage);
        Assert.Empty(Directory.GetFiles(_downloadDirectory));
    }

    // Size tolerance (ARCHITECTURE.md -> ADR-11): the libretro nightly core channel rebuilds
    // regularly and can shift a binary's size by a few bytes with no functional change. The
    // boundary below is literal, not an approximation — SizeToleranceBytes is exactly 32.
    [Fact]
    public async Task DownloadAndVerifyAsync_ContentLengthExactlyAtToleranceBoundary_Succeeds()
    {
        var bytes = new byte[50];
        new Random(1).NextBytes(bytes);
        var handler = new FakeHttpMessageHandler(_ => OkResponse(bytes));
        var service = CreateService(handler);

        var result = await service.DownloadAndVerifyAsync("https://example.com/emu.7z", "emu.7z", Sha256Hex(bytes), bytes.Length - 32);

        Assert.Equal(DownloadOutcome.Success, result.Outcome);
        Assert.NotNull(result.FilePath);
        Assert.True(File.Exists(result.FilePath));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_ContentLengthOneByteBeyondToleranceBoundary_RejectsBeforeDownloading()
    {
        var bytes = new byte[50];
        new Random(1).NextBytes(bytes);
        var handler = new FakeHttpMessageHandler(_ => OkResponse(bytes));
        var service = CreateService(handler);

        var result = await service.DownloadAndVerifyAsync("https://example.com/emu.7z", "emu.7z", Sha256Hex(bytes), bytes.Length - 33);

        Assert.Equal(DownloadOutcome.SizeExceeded, result.Outcome);
        Assert.Contains("rejected before downloading", result.ErrorMessage);
        Assert.Empty(Directory.GetFiles(_downloadDirectory));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_NoContentLengthBodyExactlyAtToleranceBoundary_Succeeds()
    {
        var bytes = new byte[50];
        new Random(1).NextBytes(bytes);
        var handler = new FakeHttpMessageHandler(_ => OkResponse(bytes, suppressContentLength: true));
        var service = CreateService(handler);

        var result = await service.DownloadAndVerifyAsync("https://example.com/emu.7z", "emu.7z", Sha256Hex(bytes), bytes.Length - 32);

        Assert.Equal(DownloadOutcome.Success, result.Outcome);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_NoContentLengthBodyOneByteBeyondToleranceBoundary_CutsOffEarlyAndDeletesFile()
    {
        var bytes = new byte[50];
        new Random(1).NextBytes(bytes);
        var handler = new FakeHttpMessageHandler(_ => OkResponse(bytes, suppressContentLength: true));
        var service = CreateService(handler);

        var result = await service.DownloadAndVerifyAsync("https://example.com/emu.7z", "emu.7z", Sha256Hex(bytes), bytes.Length - 33);

        Assert.Equal(DownloadOutcome.SizeExceeded, result.Outcome);
        Assert.Contains("larger than expected", result.ErrorMessage);
        Assert.Empty(Directory.GetFiles(_downloadDirectory));
    }

    // Domain allow-list (ARCHITECTURE.md -> ADR-26): the real security boundary against a
    // compromised manifest supplying its own DownloadUrl + matching Sha256 pair for a malicious
    // payload — the hash check alone can't catch that, since both values come from the same
    // untrusted source. Config.AllowedDownloadHosts is a compiled constant, never manifest data.
    [Fact]
    public async Task DownloadAndVerifyAsync_HostNotInAllowList_RejectsBeforeAttemptingConnection()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Should never be called — the host isn't allowed."));
        var service = CreateService(handler);

        var result = await service.DownloadAndVerifyAsync("https://not-allowed.example.org/emu.7z", "emu.7z", new string('0', 64), 10);

        Assert.Equal(DownloadOutcome.UntrustedSource, result.Outcome);
        Assert.Contains("not-allowed.example.org", result.ErrorMessage);
        Assert.Contains("isn't in EmuBridge's list of trusted download hosts", result.ErrorMessage);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_HttpSchemeWithAllowedHost_RejectsBeforeAttemptingConnection()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Should never be called — http:// is never trusted, even to an allowed host."));
        var service = CreateService(handler);

        var result = await service.DownloadAndVerifyAsync("http://example.com/emu.7z", "emu.7z", new string('0', 64), 10);

        Assert.Equal(DownloadOutcome.UntrustedSource, result.Outcome);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_MalformedUrl_RejectsAsUntrustedSourceRatherThanCrashing()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Should never be called."));
        var service = CreateService(handler);

        var result = await service.DownloadAndVerifyAsync("not a url at all", "emu.7z", new string('0', 64), 10);

        Assert.Equal(DownloadOutcome.UntrustedSource, result.Outcome);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_HttpsWithAllowedHost_ProceedsNormally()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var handler = new FakeHttpMessageHandler(_ => OkResponse(bytes));
        var service = CreateService(handler);

        var result = await service.DownloadAndVerifyAsync("https://example.com/emu.7z", "emu.7z", Sha256Hex(bytes), bytes.Length);

        Assert.Equal(DownloadOutcome.Success, result.Outcome);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_ServerReturnsError_ReturnsNetworkError()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var service = CreateService(handler);

        var result = await service.DownloadAndVerifyAsync("https://example.com/emu.7z", "emu.7z", new string('0', 64), 10);

        Assert.Equal(DownloadOutcome.NetworkError, result.Outcome);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_ReportsGrowingByteProgressAcrossMultipleChunks()
    {
        // Larger than the service's internal read buffer (80KB) so the streaming loop actually
        // runs multiple iterations — a single-chunk payload wouldn't exercise "progress over time".
        var bytes = new byte[200_000];
        new Random(42).NextBytes(bytes);
        var handler = new FakeHttpMessageHandler(_ => OkResponse(bytes));
        var service = CreateService(handler);
        var progress = new SynchronousProgress<long>();

        var result = await service.DownloadAndVerifyAsync("https://example.com/emu.7z", "emu.7z", Sha256Hex(bytes), bytes.Length, progress);

        Assert.Equal(DownloadOutcome.Success, result.Outcome);
        Assert.True(progress.Reports.Count >= 2, "Expected multiple progress reports for a payload spanning several read buffers.");
        Assert.Equal(bytes.Length, progress.Reports[^1]);
        Assert.Equal(progress.Reports, progress.Reports.OrderBy(x => x));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_TokenAlreadyCancelled_ThrowsOperationCanceledException()
    {
        var handler = new FakeHttpMessageHandler(_ => OkResponse([1, 2, 3]));
        var service = CreateService(handler);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.DownloadAndVerifyAsync("https://example.com/emu.7z", "emu.7z", new string('0', 64), 3, ct: cts.Token));
    }
}
