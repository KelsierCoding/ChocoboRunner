namespace ChocoboRunner.Models.ProtonModels;

public sealed record GameProton(
    string Name,
    string Path,
    Manifest Manifest,
    Runtime Runtime
);