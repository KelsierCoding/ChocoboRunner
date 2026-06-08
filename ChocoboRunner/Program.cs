using System.Diagnostics;
using ChocoboRunner.Cli;
using ChocoboRunner.Models;
using ChocoboRunner.Models.ProtonModels;
using ChocoboRunner.PlatformHeroic;
using ChocoboRunner.PlatformSteam;

namespace ChocoboRunner;

internal static class Program
{
    private static readonly string[] LookupSteam =
    [
        "39140",   // FF7 (2013)
        "3837340", // FF7 (2026)
        "39150"    // FF8 (2013)
    ];

    private static readonly string[] LookupGoG =
    [
        "1698970154" // FF7 (2026)
    ];

    internal static async Task Main()
    {
        if (!Environment.UserInteractive || Console.IsOutputRedirected)
        {
            return;
        }
        
        Logger.Info($@"Starting Chocobo Runner from location: {AppContext.BaseDirectory}");

        Console.Clear();

        Game game = SelectGame();

        GameProton proton = Proton.SelectProton(game);
        
        string mmInstallationPath = DirectoryPicker.Picker();

        await Confirm(game, proton, mmInstallationPath);
    }

    #region Game

    private static Game SelectGame()
    {
        Game[] games = GetGamesList();

        int maxNameLength = games.Max(g => g.Name.Length);
        List<string> display = games
            .Select(g => $"{g.Name.PadRight(maxNameLength + 2)}({g.Platform})")
            .ToList();

        Menu<string> menu = new(display)
        {
            Title = "Choose a game",
            PageSize = 15
        };

        menu.Show(askConfirmation: true);

        int index = menu.SelectedIndex;
        WriteColored(
            $"Selected game: {games[index].Name.PadRight(games[index].Name.Length + 2)}{games[index].Platform}",
            ConsoleColor.Blue);

        Game game = games[index];
        
        Logger.Info($@"Chosen game: {game.Name} {game.Platform}");
        Logger.Info($@"Games prefix location: {game.PrefixPath}");
        Logger.Info($@"Games library location: {game.LibraryPathToGame}");
        
        return game;
    }

    private static Game[] GetGamesList()
    {
        Game[] games = Heroic.EnumerateGogGames(LookupGoG)
            .Concat(Steam.EnumerateGames(LookupSteam))
            .OrderBy(g => g.Name)
            .ToArray();

        if (games.Length == 0)
        {
            Logger.Info("No games found");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("No compatible games found. Press any key to exit.");
            Console.ReadKey();
            Environment.Exit(0);
        }

        return games;
    }

    #endregion

    private static async Task Confirm(Game chosenGame, GameProton chosenProton, string mmInstallationPath)
    {
        Console.Clear();

        WriteColored(
            $"Selected game: {chosenGame.Name.PadRight(chosenGame.Name.Length + 2)}{chosenGame.Platform}",
            ConsoleColor.Blue);
        WriteColored($"Chosen proton: {chosenProton.Name}", ConsoleColor.Blue);
        WriteColored($"Chosen Mod Manager install path: {mmInstallationPath}", ConsoleColor.Blue);

        Console.WriteLine("Is this correct? (Y/Enter to confirm, N/Esc to quit)");

        while (true)
        {
            ConsoleKeyInfo keyInfo = Console.ReadKey(intercept: true);

            switch (keyInfo.Key)
            {
                case ConsoleKey.Y:
                case ConsoleKey.Enter:
                    await Installation.DoInstallation(chosenGame, chosenProton, mmInstallationPath);
                    return;

                case ConsoleKey.N:
                case ConsoleKey.Escape:
                    Environment.Exit(0);
                    return;
            }
        }
    }

    private static void WriteColored(string message, ConsoleColor color)
    {
        ConsoleColor previousColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ForegroundColor = previousColor;
    }
}