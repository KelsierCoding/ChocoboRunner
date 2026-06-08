using System.Text.RegularExpressions;
using ChocoboRunner.Models;
using ChocoboRunner.Models.ProtonModels;

namespace ChocoboRunner.PlatformSteam;

public static class Steam
{
    private const string SteamFlatpakRoot = ".var/app/com.valvesoftware.Steam";
    private const string SteamNativeRoot = ".local/share/Steam";
    private const string SteamLegacyRoot = ".steam/steam";

    public static IEnumerable<Game> EnumerateGames(string[] lookUp)
    {
        LibraryEntry[] steamLibraries = GetSteamLibraryPaths(LibrarySearchMode.All).Distinct().ToArray();

        foreach (LibraryEntry library in steamLibraries)
        {
            foreach (string file in GetFiles(library.Path, lookUp))
            {
                string appId;
                string name;
                string installPath;

                try
                {
                    string content = File.ReadAllText(file);

                    appId = ParseAcf.Parse(content, "appid");
                    name = ParseAcf.Parse(content, "name");
                    string installDir = ParseAcf.Parse(content, "installdir");

                    installPath = string.Empty;
                    if (!string.IsNullOrWhiteSpace(installDir))
                    {
                        string? manifestDirectory = Path.GetDirectoryName(file);
                        if (!string.IsNullOrWhiteSpace(manifestDirectory))
                        {
                            installPath = Path.Combine(manifestDirectory, "common", installDir);
                        }
                    }

                    if (!Directory.Exists(installPath))
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                bool isFlatpakLibrary = library.Path.StartsWith(Path.Combine(AppEnvironment.Home, SteamFlatpakRoot), StringComparison.Ordinal);
                string addition = isFlatpakLibrary ? " (flatpak)" : string.Empty;

                string prefixPath = FindPrefixPath(appId, steamLibraries, library.IsFlatpak);
                if (string.IsNullOrWhiteSpace(prefixPath))
                {
                    continue;
                }

                bool isFf8 = name.Contains("VIII", StringComparison.InvariantCultureIgnoreCase);
                string exeName = isFf8 ? "ff8_en.exe" : "ff7_en.exe";
                string modPresetName = isFf8 ? "Junction VIII" : "7th-heaven";
                string rootPath = isFlatpakLibrary
                    ? GetRoots(LibrarySearchMode.Flatpak).FirstOrDefault() ?? string.Empty
                    : GetRoots(LibrarySearchMode.Native).FirstOrDefault() ?? string.Empty;

                yield return new Game(
                    appId,
                    name,
                    $"{name} {modPresetName}",
                    $"Steam{addition}",
                    installPath,
                    exeName,
                    prefixPath,
                    library.Path,
                    rootPath);
            }
        }
    }

    private static IEnumerable<LibraryEntry> GetSteamLibraryPaths(LibrarySearchMode mode)
    {
        return GetSteamLibraryPathsFromRoots(GetRoots(mode));
    }

    private static List<string> GetRoots(LibrarySearchMode mode)
    {
        List<string> roots = [];

        if (mode is LibrarySearchMode.Native or LibrarySearchMode.All)
        {
            string nativeRoot = Path.Combine(AppEnvironment.Home, SteamNativeRoot);
            string legacyRoot = Path.Combine(AppEnvironment.Home, SteamLegacyRoot);
            string nativeLibraryConfig = Path.Combine(nativeRoot, "config", "libraryfolders.vdf");
            string legacyLibraryConfig = Path.Combine(legacyRoot, "config", "libraryfolders.vdf");

            if (Path.Exists(nativeLibraryConfig))
            {
                roots.Add(nativeRoot);
            }
            else if (Path.Exists(legacyLibraryConfig))
            {
                roots.Add(legacyRoot);
            }
        }

        if (mode is LibrarySearchMode.Flatpak or LibrarySearchMode.All)
        {
            string flatpakBase = Path.Combine(AppEnvironment.Home, SteamFlatpakRoot);
            string flatpakRoot = Path.Combine(AppEnvironment.Home, SteamFlatpakRoot, SteamNativeRoot);
            string flatpakLibraryConfig = Path.Combine(flatpakRoot, "config", "libraryfolders.vdf");
            string flatpakLegacyRoot = Path.Combine(flatpakBase, ".steam", "steam");
            string flatpakLegacyConfig = Path.Combine(flatpakLegacyRoot, "config", "libraryfolders.vdf");

            if (Path.Exists(flatpakLibraryConfig))
            {
                roots.Add(flatpakRoot);
            }
            else if (Path.Exists(flatpakLegacyConfig))
            {
                roots.Add(flatpakLegacyRoot);
            }
        }

        return roots;
    }

    private static IEnumerable<LibraryEntry> GetSteamLibraryPathsFromRoots(IEnumerable<string> roots)
    {
        foreach (string root in roots)
        {
            string libVdf = Path.Combine(root, "config", "libraryfolders.vdf");
            if (!File.Exists(libVdf))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(libVdf);
            }
            catch
            {
                continue;
            }

            foreach (Match match in Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
            {
                string path = match.Groups[1].Value;
                string steamAppsPath = Path.Combine(path, "steamapps");

                if (!Directory.Exists(steamAppsPath))
                {
                    continue;
                }

                bool isFlatpak = root.Contains(Path.Combine(AppEnvironment.Home, SteamFlatpakRoot), StringComparison.Ordinal);
                yield return new LibraryEntry(isFlatpak, steamAppsPath);
            }
        }
    }

    private static IEnumerable<string> GetFiles(string steamLibrary, string[] lookUpSteam)
    {
        foreach (string value in lookUpSteam)
        {
            string file = Path.Combine(steamLibrary, $"appmanifest_{value}.acf");
            if (File.Exists(file))
            {
                yield return file;
            }
        }
    }

    private static string FindPrefixPath(string appId, IEnumerable<LibraryEntry> steamLibraries, bool flatpak)
    {
        foreach (LibraryEntry library in steamLibraries.Where(sl => sl.IsFlatpak == flatpak))
        {
            string path = Path.Combine(library.Path, "compatdata", appId);
            if (Path.Exists(path))
            {
                return path;
            }
        }

        return string.Empty;
    }

    public static Runtime GetRuntimes(LibrarySearchMode mode, string appId)
    {
        LibraryEntry[] steamLibraries = GetSteamLibraryPaths(mode).Distinct().ToArray();

        foreach (LibraryEntry library in steamLibraries)
        {
            string manifestPath = Path.Combine(library.Path, $"appmanifest_{appId}.acf");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                string content = File.ReadAllText(manifestPath);
                string installDir = ParseAcf.Parse(content, "installdir");

                string runtimePath = Path.Combine(library.Path, "common", installDir, "run");
                if (Path.Exists(runtimePath))
                {
                    return new Runtime
                    {
                        RuntimePath = runtimePath,
                        RuntimeFound = true
                    };
                }
            }
            catch
            {
                // Ignore and continue searching other libraries.
            }
        }

        return new Runtime();
    }

    private sealed record LibraryEntry(
        bool IsFlatpak,
        string Path
    );
}