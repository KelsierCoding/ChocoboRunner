namespace ChocoboRunner;

public static class AppEnvironment
{
    public static string Home => GetHome.Value;

    public static bool DeckMode => IsDeck.Value;

    private static readonly Lazy<string> GetHome = new Lazy<string>(()=>
    {
        string? home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrEmpty(home)) return home;
        else
        {
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return !string.IsNullOrEmpty(home) ? home : Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        }
    });

    private static readonly Lazy<bool> IsDeck = new Lazy<bool>(() =>
    {
        bool osMatch = false;

        try
        {
            string osRelease = File.ReadAllText("/etc/os-release");

            osMatch = osRelease.Contains("SteamOS", StringComparison.OrdinalIgnoreCase) || osRelease.Contains("Bazzite", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            osMatch = false;
        }

        bool argMatch = Environment.GetCommandLineArgs().Any(a => a is "-d" or "--deck");

        return osMatch || argMatch;
    });
}