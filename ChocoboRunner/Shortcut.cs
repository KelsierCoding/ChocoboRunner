using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ChocoboRunner.Models;

namespace ChocoboRunner;

public class Shortcut
{
    private const string FlatpakSteamIdentifier = "com.valvesoftware.Steam";
    private const string FlatpakHeroicIdentifier = "com.heroicgameslauncher.hgl";

    public static bool Steam(string file, string steamDir, Game chosenGame)
    {
        //Because steam has to be running to create the shortcut
        //Tell the use to boot steam (I could force boot it but they might still not be logged in)
        Console.WriteLine("Do you want to add a shortcut to Steam (steam needs to be running and logged in)? (Y/n)");

        while (true)
        {
            ConsoleKeyInfo keyInfo = Console.ReadKey(true);

            switch (keyInfo.Key)
            {
                case ConsoleKey.N:
                case ConsoleKey.Escape:
                    return false;

                case ConsoleKey.Y:
                case ConsoleKey.Enter:
                    return AddNonSteamGame(file, steamDir, chosenGame);
            }
        }
    }

    private static bool AddNonSteamGame(string file, string steamDir, Game chosenGame)
    {
        Dictionary<string, string> user = GetMostRecentSteamUser() 
            ?? throw new Exception("Could not determine the current Steam user.");

        string shortcutsPath = GetShortcutsPath(chosenGame.Platform, user["UserIdFolder"]);
        string jsonString = ShortcutFileJson(shortcutsPath);

        if (ShortcutAlreadyExists(jsonString, file))
            return true;

        PrepareSteamTempPath(steamDir);
        LaunchSteamAddShortcut(file, steamDir);

        return true;
    }

    private static Dictionary<string, string>? GetMostRecentSteamUser()
    {
        string usersLogin = Path.Combine(AppEnvironment.Home, ".local/share/Steam/config/loginusers.vdf");
        List<Dictionary<string, string>> users = GetUsers(usersLogin);

        return users.FirstOrDefault(u => u.TryGetValue("MostRecent", out string? mostRecent) && mostRecent == "1");
    }

    private static string GetShortcutsPath(string platform, string userIdFolder)
    {
        if (platform.Contains("flatpak"))
        {
            return Path.Combine(
                AppEnvironment.Home,
                ".var/app/com.valvesoftware.Steam/.local/share/Steam/userdata",
                userIdFolder,
                "config/shortcuts.vdf"
            );
        }

        return Path.Combine(
            AppEnvironment.Home,
            ".local/share/Steam/userdata",
            userIdFolder,
            "config/shortcuts.vdf"
        );
    }

    private static bool ShortcutAlreadyExists(string shortcutsJson, string file)
    {
        JsonNode? root = JsonNode.Parse(shortcutsJson);
        JsonObject? shortcutsObject = root?["shortcuts"]?.AsObject();

        if (shortcutsObject == null)
            return false;

        return shortcutsObject
            .Select(kvp => kvp.Value)
            .Any(node => node?["Exe"]?.ToString().Contains(file) == true);
    }

    private static void LaunchSteamAddShortcut(string file, string steamDir)
    {
        string encodedUrl = $"steam://addnonsteamgame/{Uri.EscapeDataString(file)}";
        string command = steamDir.Contains(FlatpakSteamIdentifier)
            ? $"flatpak run {FlatpakSteamIdentifier}"
            : "steam";

        ProcessStartInfo startInfo = new()
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"nohup {command} {encodedUrl} >/dev/null 2>&1 < /dev/null &\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        //Console.WriteLine($"Running command: \"{startInfo.FileName}\" {startInfo.Arguments}");
        using Process process = new();
        process.StartInfo = startInfo;
        process.Start();
    }

    private static void PrepareSteamTempPath(string steamDir)
    {
        int uid = GetCurrentUid();

        string tmp = steamDir.Contains(FlatpakSteamIdentifier)
            ? $"/run/user/{uid}/.flatpak/{FlatpakSteamIdentifier}/tmp"
            : "/tmp";

        Directory.CreateDirectory(tmp);
        
        string markerPath = Path.Combine(tmp, "addnonsteamgamefile");
        File.WriteAllText(markerPath, string.Empty);
    }

    private static int GetCurrentUid()
    {
        ProcessStartInfo psi = new()
        {
            FileName = "id",
            ArgumentList = { "-u" },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using Process process = Process.Start(psi) ?? throw new Exception("Failed to start id process");

        string output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        return int.TryParse(output, out int uid)
            ? uid
            : throw new Exception($"Could not determine UID from output: {output}");
    }

    public static void AddDesktop(string scriptLocation, Game chosenGame, bool steamShortcutCreated, string mmIntallationPath)
    {
        Console.WriteLine("Do you want to add a shortcut to the desktop? (Y/n)");

        while (true)
        {
            ConsoleKeyInfo keyInfo = Console.ReadKey(true);

            switch (keyInfo.Key)
            {
                case ConsoleKey.N:
                case ConsoleKey.Escape:
                    return;

                case ConsoleKey.Y:
                case ConsoleKey.Enter:
                    CreateDesktopShortcut(scriptLocation, chosenGame, steamShortcutCreated, mmIntallationPath);
                    return;
            }
        }
    }

    private static void CreateDesktopShortcut(string scriptLocation, Game chosenGame, bool steamShortcutCreated, string mmIntallationPath)
    {
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string filePath = Path.Combine(desktopPath, $"{chosenGame.ShellName}.desktop");

        List<string> lines =
        [
            "[Desktop Entry]",
            $"Name={chosenGame.ShellName}.sh",
            "Comment=Play this game",
            BuildDesktopExecLine(scriptLocation, chosenGame, steamShortcutCreated),
            $"Icon={mmIntallationPath}/uninstall.ico",
            "Terminal=false",
            "Type=Application",
            "Categories=Game;"
        ];

        File.WriteAllLines(filePath, lines);

        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(filePath, UnixFileMode.UserExecute | UnixFileMode.UserWrite | UnixFileMode.UserRead);
    }

    private static string BuildDesktopExecLine(string scriptLocation, Game chosenGame, bool steamShortcutCreated)
    {
        if (chosenGame.Platform.Contains("Steam") && steamShortcutCreated)
        {
            Dictionary<string, string> user = GetMostRecentSteamUser()
                ?? throw new Exception("Could not determine the current Steam user.");

            string shortcutsPath = GetShortcutsPath(chosenGame.Platform, user["UserIdFolder"]);
            string jsonString = ShortcutFileJson(shortcutsPath);

            JsonNode? root = JsonNode.Parse(jsonString);
            JsonObject? shortcutsObject = root?["shortcuts"]?.AsObject();

            JsonNode? match = shortcutsObject?
                .Select(kvp => kvp.Value)
                .FirstOrDefault(node => node?["Exe"]?.ToString().Contains(scriptLocation) == true);

            if (match != null)
            {
                string desktopId = match["desktopShortcutId"]!.ToString();

                return chosenGame.Platform.Contains("flatpak")
                    ? $"Exec=flatpak run {FlatpakSteamIdentifier} steam://rungameid/{desktopId}"
                    : $"Exec=steam steam://rungameid/{desktopId}";
            }
        }

        if (chosenGame.Platform.Contains("flatpak"))
        {
            if (chosenGame.Platform.Contains("Steam"))
                return $"Exec=flatpak run --command=sh {FlatpakSteamIdentifier} \"{scriptLocation}\"";

            if (chosenGame.Platform.Contains("GOG"))
                return $"Exec=flatpak run --command=sh {FlatpakHeroicIdentifier} \"{scriptLocation}\"";
        }

        return $"Exec=\"{scriptLocation}\"";
    }

    private static List<Dictionary<string, string>> GetUsers(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine("Bestand niet gevonden.");
            return [];
        }

        string[] lines = File.ReadAllLines(path);
        return ParseVdfToDictionaryList(lines);
    }

    private static List<Dictionary<string, string>> ParseVdfToDictionaryList(string[] lines)
    {
        List<Dictionary<string, string>> users = [];
        Dictionary<string, string>? currentUser = null;

        Regex kvRegex = new("""([^""]+)""\s*""([^""]+)""");
        Regex idRegex = new("""(\d{17})""");

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();

            if (string.IsNullOrEmpty(line) || line == "{" || line == "}" || line == "\"users\"")
                continue;

            Match idMatch = idRegex.Match(line);
            if (idMatch.Success && !line.Contains('\t') && !line.Contains("  "))
            {
                string currentSteamId = idMatch.Groups[1].Value;
                currentUser = new Dictionary<string, string>
                {
                    { "SteamId64", currentSteamId }
                };

                if (long.TryParse(currentSteamId, out long steamId64))
                {
                    long userIdFolder = steamId64 - 76561197960265728;
                    currentUser["UserIdFolder"] = userIdFolder.ToString();
                }

                continue;
            }

            if (currentUser == null)
                continue;

            Match kvMatch = kvRegex.Match(line);
            if (kvMatch.Success)
            {
                currentUser[kvMatch.Groups[1].Value] = kvMatch.Groups[2].Value;
            }
            else if (line == "}")
            {
                users.Add(currentUser);
                currentUser = null;
            }
        }

        if (currentUser != null && !users.Contains(currentUser))
            users.Add(currentUser);

        return users;
    }

    private static string ShortcutFileJson(string path)
    {
        try
        {
            using FileStream fs = new(path, FileMode.Open, FileAccess.Read);
            using BinaryReader br = new(fs, Encoding.UTF8);
            StringBuilder json = new();

            json.AppendLine("{");

            if (br.ReadByte() == 0x00)
            {
                string rootName = ReadNullTerminatedString(br);
                json.AppendLine($"  \"{rootName}\": {{");
                ParseGroup(br, json, 2);
                json.AppendLine("  }");
            }

            json.AppendLine("}");
            return json.ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Something went wrong reading shortcuts: {ex.Message}");
            Environment.Exit(0);
            return "";
        }
    }

    private static void ParseGroup(BinaryReader br, StringBuilder json, int indentLevel)
    {
        string indent = new(' ', indentLevel * 2);
        bool firstItem = true;

        while (br.BaseStream.Position < br.BaseStream.Length)
        {
            byte typeMarker = br.ReadByte();

            if (typeMarker == 0x08)
                break;

            if (!firstItem)
                json.AppendLine(",");
            firstItem = false;

            string key = ReadNullTerminatedString(br);

            if (typeMarker == 0x00)
            {
                json.AppendLine($"{indent}\"{key}\": {{");
                ParseGroup(br, json, indentLevel + 1);
                json.Append($"{indent}}}");
            }
            else if (typeMarker == 0x01)
            {
                string value = ReadNullTerminatedString(br);
                string escapedValue = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
                json.Append($"{indent}\"{key}\": \"{escapedValue}\"");
            }
            else if (typeMarker == 0x02)
            {
                uint value = br.ReadUInt32();

                if (key == "appid")
                {
                    ulong desktopShortcutId = ((ulong)value << 32) | 0x02000000;
                    json.AppendLine($"{indent}\"appid\": {value},");
                    json.Append($"{indent}\"desktopShortcutId\": \"{desktopShortcutId}\"");
                }
                else
                {
                    json.Append($"{indent}\"{key}\": {value}");
                }
            }
        }

        json.AppendLine();
    }

    private static string ReadNullTerminatedString(BinaryReader br)
    {
        List<byte> bytes = [];

        byte b;
        while ((b = br.ReadByte()) != 0x00)
        {
            bytes.Add(b);
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}