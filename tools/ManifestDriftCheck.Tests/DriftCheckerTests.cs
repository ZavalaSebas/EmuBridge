using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Bridge.Models;

namespace ManifestDriftCheck.Tests;

public class DriftCheckerTests
{
    private static byte[] BuildZipBytes(params (string Name, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var entryStream = entry.Open();
                entryStream.Write(content);
            }
        }

        return stream.ToArray();
    }

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static List<KnownEmulator> MakeCatalog(string coreUrl, string sha256, long expectedSize, string coreFileName = "core.dll") =>
    [
        new KnownEmulator
        {
            Id = "retroarch",
            Name = "RetroArch",
            DownloadUrl = "https://example.com/retroarch.7z",
            Sha256 = "unused-in-these-tests",
            ExpectedSizeBytes = 1,
            ExecutableRelativePath = "retroarch.exe",
            Cores =
            [
                new KnownEmulatorCore
                {
                    Id = "testcore",
                    PlatformId = "nes",
                    DownloadUrl = coreUrl,
                    Sha256 = sha256,
                    ExpectedSizeBytes = expectedSize,
                    CoreFileName = coreFileName,
                    CapturedAt = "2026-07-31"
                }
            ]
        }
    ];

    [Fact]
    public async Task CheckAsync_ContentMatchesManifest_ReturnsMatch()
    {
        var bytes = BuildZipBytes(("core.dll", [1, 2, 3]));
        var handler = new FakeHttpMessageHandler(req =>
            req.RequestUri!.ToString().Contains("core.zip")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        var checker = new DriftChecker(new HttpClient(handler));
        var catalog = MakeCatalog("https://example.com/core.zip", Sha256Hex(bytes), bytes.Length);

        var results = await checker.CheckAsync(catalog);

        var coreResult = Assert.Single(results, r => r.Target.Id == "testcore");
        Assert.Equal(EntryStatus.Match, coreResult.Status);
    }

    [Fact]
    public async Task CheckAsync_HashDiffersSameSize_ReturnsDrifted()
    {
        var bytes = BuildZipBytes(("core.dll", [1, 2, 3]));
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        var checker = new DriftChecker(new HttpClient(handler));
        // Pinned hash deliberately wrong, but the real size matches — the exact "stella" pattern
        // from 2026-08-02 (ARCHITECTURE.md -> ADR-11): a same-size, different-content rebuild.
        var catalog = MakeCatalog("https://example.com/core.zip", new string('0', 64), bytes.Length);

        var results = await checker.CheckAsync(catalog);

        var coreResult = Assert.Single(results, r => r.Target.Id == "testcore");
        Assert.Equal(EntryStatus.Drifted, coreResult.Status);
        Assert.Equal(Sha256Hex(bytes), coreResult.ActualSha256);
    }

    [Fact]
    public async Task CheckAsync_ExpectedFileMissingFromArchive_ReturnsStructuralChangeNotDrifted()
    {
        // Zip is real and downloadable, but doesn't contain the CoreFileName the manifest expects
        // — same class of surprise as ADR-11's RetroArch nested-folder finding.
        var bytes = BuildZipBytes(("different_file.dll", [1, 2, 3]));
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        var checker = new DriftChecker(new HttpClient(handler));
        var catalog = MakeCatalog("https://example.com/core.zip", new string('0', 64), 3, coreFileName: "core.dll");

        var results = await checker.CheckAsync(catalog);

        var coreResult = Assert.Single(results, r => r.Target.Id == "testcore");
        Assert.Equal(EntryStatus.StructuralChange, coreResult.Status);
    }

    [Fact]
    public async Task CheckAsync_DownloadFails_ReturnsVerificationFailedNotDrifted()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var checker = new DriftChecker(new HttpClient(handler));
        var catalog = MakeCatalog("https://example.com/core.zip", new string('0', 64), 3);

        var results = await checker.CheckAsync(catalog);

        var coreResult = Assert.Single(results, r => r.Target.Id == "testcore");
        Assert.Equal(EntryStatus.VerificationFailed, coreResult.Status);
        Assert.NotNull(coreResult.Detail);
    }

    [Fact]
    public async Task CheckAsync_TwoEntriesSharingOneUrl_BothClassifiedFromOneDownload()
    {
        var bytes = BuildZipBytes(("shared.dll", [7, 8, 9]));
        var requestCountsByUrl = new Dictionary<string, int>();
        var handler = new FakeHttpMessageHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            requestCountsByUrl[url] = requestCountsByUrl.GetValueOrDefault(url) + 1;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        });
        var checker = new DriftChecker(new HttpClient(handler));
        var catalog = new List<KnownEmulator>
        {
            new()
            {
                Id = "retroarch",
                Name = "RetroArch",
                DownloadUrl = "https://example.com/retroarch.7z",
                Sha256 = "unused",
                ExpectedSizeBytes = 1,
                ExecutableRelativePath = "retroarch.exe",
                Cores =
                [
                    new KnownEmulatorCore { Id = "core_a", PlatformId = "gb", DownloadUrl = "https://example.com/shared.zip", Sha256 = Sha256Hex(bytes), ExpectedSizeBytes = bytes.Length, CoreFileName = "shared.dll", CapturedAt = "2026-07-31" },
                    new KnownEmulatorCore { Id = "core_b", PlatformId = "gbc", DownloadUrl = "https://example.com/shared.zip", Sha256 = Sha256Hex(bytes), ExpectedSizeBytes = bytes.Length, CoreFileName = "shared.dll", CapturedAt = "2026-07-31" }
                ]
            }
        };

        var results = await checker.CheckAsync(catalog);

        Assert.Equal(EntryStatus.Match, results.Single(r => r.Target.Id == "core_a").Status);
        Assert.Equal(EntryStatus.Match, results.Single(r => r.Target.Id == "core_b").Status);
        // One shared URL, two manifest entries — downloaded once, not once per entry.
        Assert.Equal(1, requestCountsByUrl["https://example.com/shared.zip"]);
    }
}
