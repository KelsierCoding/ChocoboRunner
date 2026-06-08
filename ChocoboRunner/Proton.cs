using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using ChocoboRunner.Cli;
using ChocoboRunner.Models;
using ChocoboRunner.Models.ProtonModels;
using ChocoboRunner.PlatformSteam;

namespace ChocoboRunner;

public static class Proton
{
    public static GameProton SelectProton(Game chosenGame)
    {
        GameProton[] availableProtonVersions = OrderProtonList(chosenGame);

        List<string> display = availableProtonVersions
            .Select(p => p.Name)
            .ToList();

        Menu<string> menu = new Menu<string>(display)
        {
            Title = "Choose proton version",
            PageSize = 15
        };

        menu.Show(askConfirmation: true);

        int index = menu.SelectedIndex;
        GameProton selected = availableProtonVersions[index];

        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"Chosen proton: {selected.Name}");
        Console.ResetColor();
        
        Logger.Info($@"Selected proton version: {selected.Name}");
        Logger.Info($@"Selected proton will use runner {(selected.Runtime.RuntimeFound ? selected.Runtime.RuntimePath : "None")}");

        return selected;
    }

    public static async Task Install(Game chosenGame, GameProton chosenProton, string mmInstallationPath, string mmInstallerPath)
    {
        int result;
        string error = string.Empty;

        try
        {
            ProcessResult processResult = await Launch(chosenGame, chosenProton, mmInstallationPath, mmInstallerPath);
            result = processResult.ExitCode;
        }
        catch(Exception ex)
        {
            error = ex.Message;
            result = -1;
        }

        if (result != 0)
        {
            Logger.Error($@"Something went wrong during the installation. {error}"); //Should tell to look into the log for the mod manager installer. Tho currently that one isnt created yet.
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Something went wrong during installation. Please press any key to exit.");
            Console.ResetColor();
            Console.ReadKey();
            Environment.Exit(0);
        }

        Console.WriteLine("Installation completed successfully.");
    }

    private static GameProton[] OrderProtonList(Game chosenGame)
    {
        GameProton[] protonList = GetProtonList(chosenGame);

        List<(GameProton Item, Version Version)> versioned = [];

        foreach (GameProton proton in protonList)
        {
            if (TryParseVersion(proton.Name, out Version version))
                versioned.Add((proton, version));
        }

        List<GameProton> orderedVersioned = versioned
            .OrderByDescending(x => x.Version)
            .Select(x => x.Item)
            .ToList();

        IEnumerable<GameProton> remaining = protonList
            .Except(orderedVersioned, new ProtonInfoNameComparer())
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase);

        return orderedVersioned.Concat(remaining).ToArray();
    }

    private static GameProton[] GetProtonList(Game chosenGame)
    {
        GameProton[] availableProtonVersions = GetAvailableProtonVersionsOnMachine(chosenGame).ToArray();

        if (availableProtonVersions.Length == 0)
        {
            Logger.Info($@"No compatible proton versions found for {chosenGame.Name} {chosenGame.Platform}");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("No proton versions found. Press any key to exit.");
            Console.ResetColor();
            Console.ReadKey();
            Environment.Exit(0);
        }

        return availableProtonVersions;
    }

    private static IEnumerable<GameProton> GetAvailableProtonVersionsOnMachine(Game chosenGame)
    {
        foreach (string path in GetRoots(chosenGame.Platform))
        {
            foreach (string dir in Directory.GetDirectories(path))
            {
                if (!dir.Contains("Proton", StringComparison.OrdinalIgnoreCase))
                    continue;

                string manifestPath = Path.Combine(dir, "toolmanifest.vdf");
                if (!File.Exists(manifestPath))
                    continue;

                Manifest manifest = ReadProtonManifest(manifestPath);
                Runtime runtime = GetRuntimeInfo(dir, manifest, chosenGame.Platform);

                if (!runtime.RuntimeFound)
                    continue;

                string executablePath = chosenGame.Platform.Contains("Steam", StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(dir, "proton")
                    : dir;

                yield return new GameProton(Path.GetFileName(dir), executablePath, manifest, runtime);
            }
        }
    }

    private static List<string> GetRoots(string chosenGamePlatform)
    {
        List<string> roots = [];

        string steamAppsFolderFromRoot = Path.Combine("steamapps", "common");
        string steamCompatibilityTools = Path.Combine("compatibilitytools.d");
        string gogProtonFolderFromRoot = Path.Combine("heroic", "tools", "proton");

        if (chosenGamePlatform == "Steam (flatpak)")
        {
            AddIfExists(roots, Path.Combine(AppEnvironment.Home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam", steamAppsFolderFromRoot));
            AddIfExists(roots, Path.Combine(AppEnvironment.Home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam", steamCompatibilityTools));
        }
        else if (chosenGamePlatform == "Steam")
        {
            AddIfExists(roots, Path.Combine(AppEnvironment.Home, ".local", "share", "Steam", steamAppsFolderFromRoot));
            if (roots.Count == 0)
                AddIfExists(roots, Path.Combine(AppEnvironment.Home, ".steam", "steam", steamAppsFolderFromRoot));

            AddIfExists(roots, Path.Combine(AppEnvironment.Home, ".local", "share", "Steam", steamCompatibilityTools));
            AddIfExists(roots, Path.Combine(Path.DirectorySeparatorChar.ToString(), "usr", "share", "steam", steamCompatibilityTools));
        }
        else if (chosenGamePlatform == "GOG (flatpak)")
        {
            AddIfExists(roots, Path.Combine(AppEnvironment.Home, ".var", "app", "com.heroicgameslauncher.hgl", "config", gogProtonFolderFromRoot));
            AddIfExists(roots, Path.Combine(AppEnvironment.Home, ".local", "share", "Steam", "compatibilitytools.d"));
        }
        else if (chosenGamePlatform == "GOG")
        {
            AddIfExists(roots, Path.Combine(AppEnvironment.Home, ".config", gogProtonFolderFromRoot));
            AddIfExists(roots, Path.Combine(Path.DirectorySeparatorChar.ToString(), "usr", "share", "steam", "compatibilitytools.d"));
            AddIfExists(roots, Path.Combine(AppEnvironment.Home, ".local", "share", "Steam", "compatibilitytools.d"));
        }

        return roots;
    }

    private static void AddIfExists(List<string> roots, string path)
    {
        if (Path.Exists(path))
            roots.Add(path);
    }

    private static Manifest ReadProtonManifest(string path)
    {
        string data = File.ReadAllText(path);

        return new Manifest
        {
            Version = ParseAcf.Parse(data, "version"),
            CommandLine = ParseAcf.Parse(data, "commandline"),
            RequireToolAppId = ParseAcf.Parse(data, "require_tool_appid"),
            UseSessions = ParseAcf.Parse(data, "use_sessions"),
            CompatManagerLayerName = ParseAcf.Parse(data, "compatmanager_layer_name")
        };
    }

    private static Runtime GetRuntimeInfo(string dir, Manifest manifest, string chosenGamePlatform)
    {
        Runtime runtime = new();

        if (chosenGamePlatform.Contains("GOG", StringComparison.OrdinalIgnoreCase))
        {
            if (!chosenGamePlatform.Contains("flatpak", StringComparison.OrdinalIgnoreCase) &&
                Path.Exists(Path.Combine(Path.DirectorySeparatorChar.ToString(), "usr", "bin", "umu-run")))
            {
                runtime.RuntimePath = Path.Combine("/usr", "bin", "umu-run");
                runtime.RuntimeFound = true;
                return runtime;
            }

            if (!chosenGamePlatform.Contains("flatpak", StringComparison.OrdinalIgnoreCase) &&
                Path.Exists(Path.Combine(AppEnvironment.Home, ".config", "heroic", "tools", "runtimes", "umu", "umu-run")))
            {
                runtime.RuntimePath = Path.Combine(AppEnvironment.Home, ".config", "heroic", "tools", "runtimes", "umu", "umu-run");
                runtime.RuntimeFound = true;
                return runtime;
            }

            if (Path.Exists(Path.Combine(AppEnvironment.Home, ".var", "app", "com.heroicgameslauncher.hgl", "config", "heroic", "tools", "runtimes", "umu", "umu-run")) &&
                !dir.Contains(Path.Combine(AppEnvironment.Home, ".config")))
            {
                runtime.RuntimePath = Path.Combine(AppEnvironment.Home, ".var", "app", "com.heroicgameslauncher.hgl", "config", "heroic", "tools", "runtimes", "umu", "umu-run");
                runtime.RuntimeFound = true;
                return runtime;
            }

            runtime.RuntimePath = string.Empty;
            runtime.RuntimeFound = false;
            return runtime;
        }

        if (string.IsNullOrWhiteSpace(manifest.RequireToolAppId))
        {
            runtime.RuntimePath = string.Empty;
            runtime.RuntimeFound = true;
            return runtime;
        }

        return Steam.GetRuntimes(
            chosenGamePlatform.Contains("flatpak", StringComparison.OrdinalIgnoreCase)
                ? LibrarySearchMode.Flatpak
                : LibrarySearchMode.Native,
            manifest.RequireToolAppId);
    }

    private static async Task<ProcessResult> Launch(
        Game chosenGame,
        GameProton chosenProtonVersion,
        string installationPath,
        string installerPath,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chosenProtonVersion.Runtime.RuntimePath))
            return await LaunchWithNoRunner(chosenGame, chosenProtonVersion, installationPath, installerPath, timeout, cancellationToken);

        return await LaunchWithRunner(chosenGame, chosenProtonVersion, installationPath, installerPath, timeout, cancellationToken);
    }

    private static async Task<ProcessResult> LaunchWithNoRunner(
        Game chosenGame,
        GameProton chosenProtonVersion,
        string installationPath,
        string installerPath,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        Logger.Info("Trying to install without runner");
        
        ProcessStartInfo startInfo = new()
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (chosenGame.Platform.Contains("flatpak", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = "flatpak";
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add($"--command={chosenProtonVersion.Path}");

            if (chosenGame.Platform.Contains("Steam", StringComparison.OrdinalIgnoreCase))
            {
                await SetFlatpakOverride("com.valvesoftware.Steam", installationPath, timeout, cancellationToken);
                startInfo.ArgumentList.Add("com.valvesoftware.Steam");
            }
            else if (chosenGame.Platform.Contains("GOG", StringComparison.OrdinalIgnoreCase))
            {
                await SetFlatpakOverride("com.heroicgameslauncher.hgl", installationPath, timeout, cancellationToken);
                startInfo.ArgumentList.Add("com.heroicgameslauncher.hgl");
            }
            else
            {
                return new ProcessResult { ExitCode = -1 };
            }
        }
        else
        {
            startInfo.FileName = chosenProtonVersion.Path;
        }

        SetSteamArgumentsAndEnvironmentVariables(startInfo, chosenGame, installationPath, installerPath);

        Console.WriteLine($"Running in the background {installerPath} please wait...");
        return await RunProcess(startInfo, timeout, cancellationToken);
    }

    private static async Task<ProcessResult> LaunchWithRunner(
        Game chosenGame,
        GameProton chosenProtonVersion,
        string installationPath,
        string installerPath,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        Logger.Info("Trying to install with runner");
        
        ProcessStartInfo startInfo = new()
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (chosenGame.Platform.Contains("flatpak", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = "flatpak";
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add($"--command={chosenProtonVersion.Runtime.RuntimePath}");

            if (chosenGame.Platform.Contains("Steam", StringComparison.OrdinalIgnoreCase))
            {
                await SetFlatpakOverride("com.valvesoftware.Steam", installationPath, timeout, cancellationToken);
                startInfo.ArgumentList.Add("com.valvesoftware.Steam");
                startInfo.ArgumentList.Add("--");
                startInfo.ArgumentList.Add(chosenProtonVersion.Path);
                SetSteamArgumentsAndEnvironmentVariables(startInfo, chosenGame, installationPath, installerPath);
            }
            else if (chosenGame.Platform.Contains("GOG", StringComparison.OrdinalIgnoreCase))
            {
                await SetFlatpakOverride("com.heroicgameslauncher.hgl", installationPath, timeout, cancellationToken);
                startInfo.ArgumentList.Add("com.heroicgameslauncher.hgl");
                SetGogArgumentsAndEnvironmentVariables(startInfo, chosenGame, chosenProtonVersion, installationPath, installerPath);
            }
            else
            {
                return new ProcessResult { ExitCode = -1 };
            }
        }
        else
        {
            startInfo.FileName = chosenProtonVersion.Runtime.RuntimePath;

            if (chosenGame.Platform.Contains("Steam", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.ArgumentList.Add("--");
                startInfo.ArgumentList.Add(chosenProtonVersion.Path);
                SetSteamArgumentsAndEnvironmentVariables(startInfo, chosenGame, installationPath, installerPath);
            }
            else if (chosenGame.Platform.Contains("GOG", StringComparison.OrdinalIgnoreCase))
            {
                SetGogArgumentsAndEnvironmentVariables(startInfo, chosenGame, chosenProtonVersion, installationPath, installerPath);
            }
            else
            {
                return new ProcessResult { ExitCode = -1 };
            }
        }

        Console.WriteLine($"Running in the background {installerPath} please wait...");
        return await RunProcess(startInfo, timeout, cancellationToken);
    }

    private static async Task SetFlatpakOverride(string package, string installationPath, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "flatpak",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("override");
        startInfo.ArgumentList.Add("--user");
        startInfo.ArgumentList.Add($"--filesystem={installationPath}");
        startInfo.ArgumentList.Add(package);

        await RunProcess(startInfo, timeout, cancellationToken);
    }

    private static async Task<ProcessResult> RunProcess(ProcessStartInfo startInfo, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        using Process process = new();
        process.StartInfo = startInfo;
        process.EnableRaisingEvents = true;

        StringBuilder stdoutBuilder = new();
        StringBuilder stderrBuilder = new();

        TaskCompletionSource<bool> stdoutClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> stderrClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null)
                stdoutClosed.TrySetResult(true);
            else
            {
                //stdoutBuilder.AppendLine(e.Data);
                //Console.WriteLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null)
                stderrClosed.TrySetResult(true);
            else
            {
                //stderrBuilder.AppendLine(e.Data);
                //Console.Error.WriteLine(e.Data);
            }
        };

        try
        {
            
            //string printableArguments = string.Join(" ", startInfo.ArgumentList.Select(arg => $"\"{arg}\""));
            //Console.WriteLine($"Running command: \"{startInfo.FileName}\" {printableArguments}");
            if (!process.Start())
                throw new InvalidOperationException("Failed to start process.");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            Task waitForExitTask = process.WaitForExitAsync(cancellationToken);
            Task allStreamsClosed = Task.WhenAll(waitForExitTask, stdoutClosed.Task, stderrClosed.Task);

            if (timeout.HasValue)
            {
                using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout.Value);

                try
                {
                    await Task.WhenAny(allStreamsClosed, Task.Delay(Timeout.Infinite, timeoutCts.Token));
                }
                catch (OperationCanceledException)
                {
                }
            }
            else
            {
                await allStreamsClosed;
            }

            ProcessResult result = new()
            {
                ExitCode = process.HasExited ? process.ExitCode : -1,
                StdOut = stdoutBuilder.ToString(),
                StdErr = stderrBuilder.ToString(),
                TimedOut = !process.HasExited
            };

            if (process.HasExited || !timeout.HasValue)
                return result;

            try { process.Kill(true); } catch { }
            return result;
        }
        catch
        {
            if (!process.HasExited)
            {
                try { process.Kill(true); } catch { }
            }

            throw;
        }
    }

    private static void SetSteamArgumentsAndEnvironmentVariables(
        ProcessStartInfo startInfo,
        Game chosenGame,
        string installationPath,
        string installerPath)
    {
        startInfo.ArgumentList.Add("waitforexitandrun");
        startInfo.ArgumentList.Add(installerPath);
        startInfo.ArgumentList.Add("/VERYSILENT");
        startInfo.ArgumentList.Add($"/DIR=Z:{installationPath}");

        startInfo.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = chosenGame.Root;
        startInfo.Environment["STEAM_COMPAT_DATA_PATH"] = chosenGame.PrefixPath;
        startInfo.Environment["STEAM_COMPAT_INSTALL_PATH"] = chosenGame.InstallPath;
        startInfo.Environment["STEAM_COMPAT_LIBRARY_PATHS"] = chosenGame.LibraryPathToGame;
        startInfo.Environment["PROTON_SET_GAME_DRIVE"] = "1";
        startInfo.Environment["WINEDLLOVERRIDES"] = "dinput=n,b";
    }

    private static void SetGogArgumentsAndEnvironmentVariables(
        ProcessStartInfo startInfo,
        Game chosenGame,
        GameProton chosenProton,
        string installationPath,
        string installerPath)
    {
        startInfo.ArgumentList.Add(installerPath);
        startInfo.ArgumentList.Add("/VERYSILENT");
        startInfo.ArgumentList.Add($"/DIR=Z:{installationPath}");

        startInfo.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = $"/home/{Environment.UserName}/.local/steam";
        startInfo.Environment["STEAM_COMPAT_DATA_PATH"] = chosenGame.PrefixPath;
        startInfo.Environment["STEAM_COMPAT_INSTALL_PATH"] = chosenGame.InstallPath;
        startInfo.Environment["STEAM_COMPAT_LIBRARY_PATHS"] = chosenGame.InstallPath;

        startInfo.Environment["GAMEID"] = "0";
        startInfo.Environment["WINEPREFIX"] = chosenGame.PrefixPath;
        startInfo.Environment["PROTONPATH"] = chosenProton.Path;
        startInfo.Environment["PROTON_VERB"] = "waitforexitandrun";
        startInfo.Environment["STORE"] = "gog";
        startInfo.Environment["LD_PRELOAD"] = "";
        startInfo.Environment["PROTON_SET_GAME_DRIVE"] = "1";
        startInfo.Environment["WINEDLLOVERRIDES"] = "dinput=n,b";
    }

    #region GameProtonHelper

    private static bool TryParseVersion(string name, out Version version)
    {
        version = null!;

        if (string.IsNullOrWhiteSpace(name))
            return false;

        Match match = Regex.Match(name, @"^\s*Proton\s+(\d+)(?:\.(\d+))?\b", RegexOptions.IgnoreCase);
        if (!match.Success)
            return false;

        if (!int.TryParse(match.Groups[1].Value, out int major))
            return false;

        int minor = 0;
        if (match.Groups[2].Success)
            int.TryParse(match.Groups[2].Value, out minor);

        version = new Version(major, minor);
        return true;
    }

    private sealed class ProtonInfoNameComparer : IEqualityComparer<GameProton>
    {
        public bool Equals(GameProton? x, GameProton? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;

            return string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(GameProton obj)
        {
            return obj.Name.ToUpperInvariant().GetHashCode();
        }
    }

    #endregion
}