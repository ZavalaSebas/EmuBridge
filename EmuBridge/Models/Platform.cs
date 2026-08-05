namespace EmuBridge.Models;

public class Platform
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> Extensions { get; set; } = [];
}
