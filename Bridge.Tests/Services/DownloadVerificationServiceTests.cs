using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Bridge.Models;
using Bridge.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bridge.Tests.Services;

public class DownloadVerificationServiceTests : IDisposable
{
    private readonly string _downloadDirectory;

    public DownloadVerificationServiceTests()
    {
        _downloadDirectory = Path.Combine(Path.GetTempPath(), $"bridge_download_test_{Guid.NewGuid()}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_downloadDirectory))
        {
            Directory.Delete(_downloadDirectory, recursive: true);
        }
    }

    private DownloadVerificationService CreateService(FakeHttpMessageHandler handler)
        => new(new HttpClient(handler), _downloadDirectory, NullLogger<DownloadVerificationService>.Instance);

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

    [Fact]
    public async Task DownloadAndVerifyAsync_ServerReturnsError_ReturnsNetworkError()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var service = CreateService(handler);

        var result = await service.DownloadAndVerifyAsync("https://example.com/emu.7z", "emu.7z", new string('0', 64), 10);

        Assert.Equal(DownloadOutcome.NetworkError, result.Outcome);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_TokenAlreadyCancelled_ThrowsOperationCanceledException()
    {
        var handler = new FakeHttpMessageHandler(_ => OkResponse([1, 2, 3]));
        var service = CreateService(handler);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.DownloadAndVerifyAsync("https://example.com/emu.7z", "emu.7z", new string('0', 64), 3, cts.Token));
    }
}
