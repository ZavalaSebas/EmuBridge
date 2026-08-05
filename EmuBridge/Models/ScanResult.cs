namespace EmuBridge.Models;

public class ScanResult
{
    public int GamesAdded { get; set; }
    public int GamesUpdated { get; set; }
    public int GamesMarkedMissing { get; set; }
    public List<SkippedFolder> SkippedFolders { get; set; } = [];
    public List<SkippedFile> SkippedFiles { get; set; } = [];
}

public record SkippedFolder(string Path, string Reason);

public record SkippedFile(string Path, string Reason);
