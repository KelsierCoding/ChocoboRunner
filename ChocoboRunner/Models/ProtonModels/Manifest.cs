namespace ChocoboRunner.Models.ProtonModels;

public class Manifest
{
    public string Version { get; set; } = "";
    
    public string CommandLine { get; set; } = "";

    public string RequireToolAppId { get; set; } = "";

    public string UseSessions { get; set; } = "";
    
    public string CompatManagerLayerName { get; set; } = "";
}