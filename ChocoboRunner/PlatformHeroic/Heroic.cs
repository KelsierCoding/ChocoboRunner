using System.Text.Json;
using ChocoboRunner.Models;

namespace ChocoboRunner.PlatformHeroic;

public class Heroic
{
    public static IEnumerable<Game> EnumerateGogGames(string[] lookup)
    {
        HashSet<string> lookupSet = lookup.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string libraryPath in GetRoots(LibrarySearchMode.All))
        {
            if (!Directory.Exists(libraryPath))
                continue;

            string installedJsonPath = Path.Combine(libraryPath, "gog_store", "installed.json");
            if (!File.Exists(installedJsonPath))
                continue;

            List<Game> games = [];

            try
            {
                using FileStream installedJson = File.OpenRead(installedJsonPath);
                using JsonDocument installedDoc = JsonDocument.Parse(installedJson);

                if (!installedDoc.RootElement.TryGetProperty("installed", out JsonElement installed) ||
                    installed.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement node in installed.EnumerateArray())
                {
                    if (TryGetGameFromNode(libraryPath, installedJsonPath, node, lookupSet, out Game? game))
                    {
                        games.Add(game!);
                    }
                }
            }
            catch
            {
                // ignored
            }

            foreach (Game game in games)
            {
                yield return game;
            }
        }
    }

    private static bool TryGetGameFromNode(
        string libraryPath,
        string installedJsonPath,
        JsonElement node,
        HashSet<string> lookupSet,
        out Game? game)
    {
        game = null;

        if (!node.TryGetProperty("appName", out JsonElement appNameElement))
            return false;

        string id = appNameElement.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(id) || !lookupSet.Contains(id))
            return false;

        string gameConfigPath = Path.Combine(libraryPath, "GamesConfig", $"{id}.json");
        if (!File.Exists(gameConfigPath))
            return false;

        string prefix;
        try
        {
            using FileStream gameConfig = File.OpenRead(gameConfigPath);
            using JsonDocument gameConfigDoc = JsonDocument.Parse(gameConfig);

            if (!gameConfigDoc.RootElement.TryGetProperty(id, out JsonElement gameElement) ||
                !gameElement.TryGetProperty("winePrefix", out JsonElement winePrefixElement))
            {
                return false;
            }

            prefix = winePrefixElement.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(prefix))
                return false;
        }
        catch
        {
            return false;
        }

        if (!node.TryGetProperty("install_path", out JsonElement installPathElement))
            return false;

        string installPath = installPathElement.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(installPath))
            return false;

        string name = Path.GetFileName(prefix);
        bool isFf8 = name.Contains("VIII", StringComparison.InvariantCultureIgnoreCase);
        string launcher = isFf8 ? "Junction VIII" : "7th-heaven";
        string executable = isFf8 ? "ff8_en.exe" : "ff7_en.exe";
        string source = $"GOG{GetFlatpakSuffix(libraryPath)}";

        game = new Game(
            id,
            name,
            $"{name} {launcher}",
            source,
            installPath,
            executable,
            prefix,
            Directory.GetParent(installedJsonPath)?.FullName ?? string.Empty,
            string.Empty);

        return true;
    }

    private static string GetFlatpakSuffix(string libraryPath)
    {
        string flatpakRoot = Path.Combine(AppEnvironment.Home, ".var", "app", "com.heroicgameslauncher.hgl");
        return libraryPath.StartsWith(flatpakRoot, StringComparison.Ordinal) ? " (flatpak)" : string.Empty;
    }

    private static List<string> GetRoots(LibrarySearchMode mode)
    {
        List<string> roots = [];

        string nativeRoot = Path.Combine(AppEnvironment.Home, ".config", "heroic");
        string flatpakRoot = Path.Combine(AppEnvironment.Home, ".var", "app", "com.heroicgameslauncher.hgl", "config", "heroic");

        if (mode is LibrarySearchMode.Native or LibrarySearchMode.All && Directory.Exists(nativeRoot))
            roots.Add(nativeRoot);

        if (mode is LibrarySearchMode.Flatpak or LibrarySearchMode.All && Directory.Exists(flatpakRoot))
            roots.Add(flatpakRoot);

        return roots;
    }
}