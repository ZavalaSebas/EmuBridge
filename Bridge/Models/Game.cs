namespace Bridge.Models;

public class Game
{
    public Guid Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PlatformId { get; set; } = string.Empty;
    public bool IsMissing { get; set; }
}
