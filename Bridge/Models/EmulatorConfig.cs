namespace Bridge.Models;

public class EmulatorConfig
{
    public Guid Id { get; set; }
    public string PlatformId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string ArgumentTemplate { get; set; } = string.Empty;
}
