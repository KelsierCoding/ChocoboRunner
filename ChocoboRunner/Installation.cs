using System.Reflection;
using ChocoboRunner.Models;
using ChocoboRunner.Models.ProtonModels;

namespace ChocoboRunner;

public static class Installation
{
    private const string DxvkConfigContent = "d3d9.shaderModel = 1";

    public static async Task DoInstallation(Game chosenGame, GameProton chosenProton, string mmInstallationPath)
    {
        string mmInstallerPath = await GetInstalledPath(chosenGame, mmInstallationPath);

        await Proton.Install(chosenGame, chosenProton, mmInstallationPath, mmInstallerPath);
        await ApplyPatches(chosenGame, mmInstallationPath);
        await CreateSteamAppId(chosenGame, mmInstallationPath);

        await File.WriteAllTextAsync(Path.Combine(mmInstallationPath, "dxvk.conf"), DxvkConfigContent);

        string scriptLocation = await CreateLauncherSh(chosenGame, chosenProton, mmInstallationPath);

        Console.ResetColor();

        bool steamShortcutCreated = Shortcut.Steam(scriptLocation, chosenGame.Root, chosenGame);

        Shortcut.AddDesktop(scriptLocation, chosenGame, steamShortcutCreated, mmInstallationPath);
        CopyTimeout(chosenGame);

        // TODO: Add steam controller stuff for deck
        SetupControllerForSteamDeck(steamShortcutCreated, chosenGame);
    }

    private static async Task<string> GetInstalledPath(Game chosenGame, string mmInstallationPath)
    {
        try
        {
            Console.ForegroundColor = ConsoleColor.Green;

            string repo = chosenGame.AppId == "39150"
                ? "tsunamods-codes/Junction-VIII"
                : "tsunamods-codes/7th-Heaven";

            string tmpPath = Path.Combine(mmInstallationPath, "tmp");

            Logger.Info("Starting download process");
            Console.WriteLine("Starting download process please wait...");
            
            string installerPath = await Github.DownloadLatestReleaseAssetAsync(
                repo,
                assetPredicate: a => a.BrowserDownloadUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase),
                null,
                tmpPath);

            if (string.IsNullOrWhiteSpace(installerPath))
                throw new InvalidOperationException("Installer path not found");

            return installerPath;
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to obtain installer");
            Logger.Error(ex.ToString());
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Failed to download installation: {ex.Message}");
            Console.WriteLine("Press any key to exit");
            Console.ReadKey();
            Environment.Exit(0);
            return string.Empty;
        }
    }

    private static async Task ApplyPatches(Game chosenGame, string mmInstallationPath)
    {
        Console.WriteLine("Patching....");

        string workshop = chosenGame.AppId == "39150"
            ? "J8Workshop"
            : "7thWorkshop";

        MoveSettingsToDisk(chosenGame.GameExeName, mmInstallationPath, workshop);
        await UpdateSettings(mmInstallationPath, workshop, chosenGame);
    }

    private static void MoveSettingsToDisk(string exeName, string targetPath, string workshop)
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        string? resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith($"{exeName.Split('_')[0]}.xml", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            Fail("Failed to find embedded settings XML resource.");
            return;
        }

        try
        {
            using Stream rs = asm.GetManifestResourceStream(resourceName)
                              ?? throw new InvalidOperationException($"Resource stream not found: {resourceName}");

            string workshopPath = Path.Combine(targetPath, workshop);
            Directory.CreateDirectory(workshopPath);

            string settingsPath = Path.Combine(workshopPath, "settings.xml");
            using FileStream fs = File.Create(settingsPath);
            rs.CopyTo(fs);
        }
        catch (Exception ex)
        {
            Fail($"Failed to move settings to the required location: {ex.Message}");
        }
    }

    private static async Task UpdateSettings(string targetPath, string workshop, Game chosenGame)
    {
        string settingsPath = Path.Combine(targetPath, workshop, "settings.xml");
        string content = await File.ReadAllTextAsync(settingsPath);

        Directory.CreateDirectory(Path.Combine(targetPath, "mods"));
        content = content.Replace("{MODLIBRARYPATH}", $@"Z:{Path.Combine(targetPath, "mods")}");

        (string exePath, string platform) = GetGameExecutableAndPlatform(chosenGame);
        content = content.Replace("{GAMEEXEPATH}", exePath);
        content = content.Replace("{PLATFORM}", platform);
        content = content.Replace("{WINDOWSTATE}", AppEnvironment.DeckMode ? "Maximized" : "Normal");

        await File.WriteAllTextAsync(settingsPath, content);
    }

    private static (string ExePath, string Platform) GetGameExecutableAndPlatform(Game chosenGame)
    {
        string steamPrefix = chosenGame.InstallPath.Contains("/steamapps/", StringComparison.OrdinalIgnoreCase)
            ? chosenGame.InstallPath[chosenGame.InstallPath.IndexOf("/steamapps/", StringComparison.OrdinalIgnoreCase)..]
            : chosenGame.InstallPath;

        return chosenGame.AppId switch
        {
            "39140" => ($@"S:{steamPrefix}/ff7_en.exe", "Steam"),
            "39150" => ($@"S:{steamPrefix}/FF8_EN.exe", "Steam"),
            "3837340" => ($@"S:{steamPrefix}/ff7/workingdir/ff7_en.exe", "SteamReRelease"),
            "1698970154" => (@"S:/ff7/workingdir/ff7_en.exe", "GOG"),
            _ => (string.Empty, string.Empty)
        };
    }

    private static async Task CreateSteamAppId(Game chosenGame, string targetPath)
    {
        await File.WriteAllTextAsync(Path.Combine(targetPath, "steam_appid.txt"), chosenGame.AppId);

        switch (chosenGame.AppId)
        {
            case "3837340":
            case "1698970154":
                await File.WriteAllTextAsync(Path.Combine(chosenGame.InstallPath, "ff7", "workingdir", "steam_appid.txt"), chosenGame.AppId);
                break;

            case "39140":
            case "39150":
                await File.WriteAllTextAsync(Path.Combine(chosenGame.InstallPath, "steam_appid.txt"), chosenGame.AppId);
                break;
        }
    }

    private static async Task<string> CreateLauncherSh(Game chosenGame, GameProton chosenProton, string mmInstallationPath)
    {
        string path = Path.Combine(mmInstallationPath, $"{chosenGame.ShellName}.sh");
        List<string> lines = new List<string> { "#!/bin/sh", "" };

        if (chosenGame.Platform.Contains("Steam"))
        {
            AddSteamLauncherLines(lines, chosenGame, chosenProton, mmInstallationPath);
        }
        else if (chosenGame.Platform.Contains("GOG"))
        {
            AddGogLauncherLines(lines, chosenGame, chosenProton, mmInstallationPath);
        }

        try
        {
            await File.WriteAllLinesAsync(path, lines);
            
            if(OperatingSystem.IsLinux())
                File.SetUnixFileMode(path, UnixFileMode.UserExecute | UnixFileMode.UserWrite | UnixFileMode.UserRead);
            
            return path;
        }
        catch (Exception ex)
        {
            Fail($"Failed to generate or set permissions for the sh file: {ex.Message}");
            return string.Empty;
        }
    }

    private static void AddSteamLauncherLines(List<string> lines, Game chosenGame, GameProton chosenProton, string mmInstallationPath)
    {
        lines.Add($"export STEAM_COMPAT_APP_ID={chosenGame.AppId}");
        lines.Add($"export STEAM_COMPAT_CLIENT_INSTALL_PATH=\"{chosenGame.LibraryPathToGame.Replace("/steamapps", "")}\"");
        lines.Add($"export STEAM_COMPAT_DATA_PATH=\"{chosenGame.PrefixPath}\"");
        lines.Add($"export STEAM_COMPAT_INSTALL_PATH=\"{chosenGame.InstallPath}\"");
        lines.Add($"export STEAM_COMPAT_LIBRARY_PATHS=\"{chosenGame.LibraryPathToGame.Replace("/steamapps", "")}\"");
        lines.Add("export PROTON_SET_GAME_DRIVE=1");
        lines.Add("export WINEDLLOVERRIDES=\"dinput=n,b\"");
        lines.Add("export DXVK_HDR=0");
        lines.Add("");
        lines.Add("export PATH=$(echo \"${PATH}\" | sed -e \"s|:$HOME/dotnet||\")");
        lines.Add("unset DOTNET_ROOT");
        lines.Add("");

        string launcherExe = chosenGame.Name.Contains("VIII")
            ? "Junction VIII.exe"
            : "7th Heaven.exe";

        lines.Add(string.IsNullOrWhiteSpace(chosenProton.Runtime.RuntimePath) 
            ? $"\"{chosenProton.Path}\" waitforexitandrun \"{mmInstallationPath}/{launcherExe}\" $@" 
            : $"\"{chosenProton.Runtime.RuntimePath}\" -- \"{chosenProton.Path}\" waitforexitandrun \"{mmInstallationPath}/{launcherExe}\" $@");
    }

    private static void AddGogLauncherLines(List<string> lines, Game chosenGame, GameProton chosenProton, string mmInstallationPath)
    {
        lines.Add($"export STEAM_COMPAT_CLIENT_INSTALL_PATH=\"{AppEnvironment.Home}/.local/steam\"");
        lines.Add($"export STEAM_COMPAT_DATA_PATH=\"{chosenGame.PrefixPath}\"");
        lines.Add($"export STEAM_COMPAT_INSTALL_PATH=\"{chosenGame.InstallPath}\"");
        lines.Add($"export STEAM_COMPAT_LIBRARY_PATHS=\"{chosenGame.InstallPath}\"");
        lines.Add("export GAMEID=0");
        lines.Add($"export WINEPREFIX=\"{chosenGame.PrefixPath}\"");
        lines.Add($"export PROTONPATH=\"{chosenProton.Path}\"");
        lines.Add("export PROTON_VERB=\"waitforexitandrun\"");
        lines.Add("export STORE=gog");
        lines.Add("export PROTON_SET_GAME_DRIVE=1");
        lines.Add("export WINEDLLOVERRIDES=\"dinput=n,b\"");
        lines.Add("export DXVK_HDR=0");
        lines.Add("unset LD_PRELOAD");
        lines.Add("");
        lines.Add("export PATH=$(echo \"${PATH}\" | sed -e \"s|:$HOME/dotnet||\")");
        lines.Add("unset DOTNET_ROOT");
        lines.Add("");

        if (chosenGame.Platform.Contains("flatpak"))
        {
            lines.Add($"flatpak run --command=\"{chosenProton.Runtime.RuntimePath}\" com.heroicgameslauncher.hgl \"{mmInstallationPath}/7th Heaven.exe\" $@");
        }
        else
        {
            lines.Add($"\"{chosenProton.Runtime.RuntimePath}\" \"{mmInstallationPath}/7th Heaven.exe\" $@");
        }
    }

    private static void CopyTimeout(Game chosenGame)
    {
        string basePath = chosenGame.PrefixPath;
        string lastFolder = Path.GetFileName(chosenGame.PrefixPath);

        if (!string.Equals(lastFolder, "pfx", StringComparison.OrdinalIgnoreCase))
        {
            basePath = Path.Combine(basePath, "pfx");
        }

        string endPath = Path.Combine(basePath, "drive_c", "windows", "system32", "timeout.exe");
        if (File.Exists(endPath)) return;

        Assembly asm = Assembly.GetExecutingAssembly();
        string? resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("timeout.exe", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            Fail("Failed to find embedded timeout.exe resource.");
            return;
        }

        using Stream rs = asm.GetManifestResourceStream(resourceName)
                          ?? throw new InvalidOperationException($"Resource stream not found: {resourceName}");

        Directory.CreateDirectory(Path.GetDirectoryName(endPath)!); // fixed bug
        using FileStream fs = File.Create(endPath);
        rs.CopyTo(fs);
    }
    
    private static void SetupControllerForSteamDeck(bool steamShortcutCreated, Game chosenGame)
    {
        if (!AppEnvironment.DeckMode) return;
        if (!steamShortcutCreated && chosenGame.AppId == "1698970154") return;

        Console.WriteLine("Do you want to add controller config? (Y/Enter to confirm, N/Esc to cancel)");
        
        while (true)
        {
            ConsoleKeyInfo keyInfo = Console.ReadKey(intercept: true);

            switch (keyInfo.Key)
            {
                case ConsoleKey.Y:
                case ConsoleKey.Enter:
                    Logger.Info("User opted for controller setup");
                    //Sadly I dont know how the controller config work yet
                    //Or have a way to really test it
                    return;

                case ConsoleKey.N:
                case ConsoleKey.Escape:
                    Logger.Info("User opted out of controller setup");
                    return;
            }
        }
    }

    private static void Fail(string message)
    {
        Logger.Error(message);
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.WriteLine("Press any key to exit");
        Console.ReadKey();
        Environment.Exit(0);
    }
}