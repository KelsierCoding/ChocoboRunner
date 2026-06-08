namespace ChocoboRunner.Models;

public sealed record Game(
    string AppId,
    string Name,
    string ShellName,
    string Platform,
    string InstallPath,
    string GameExeName,
    string PrefixPath,
    string LibraryPathToGame,
    string Root
);