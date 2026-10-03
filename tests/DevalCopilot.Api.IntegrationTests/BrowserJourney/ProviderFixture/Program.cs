using DevalCopilot.Api.IntegrationTests.BrowserJourney.ProviderFixture;

// One executable, three roles chosen by the file name it is launched under (codex, claude, verify): the host copies the
// apphost under those names into the owned root's bin directory. It never inherits configuration: every path it trusts is
// derived from its own location and checked for ownership and aliases before anything is read or written.
var roleName = Path.GetFileNameWithoutExtension(Environment.ProcessPath);
try
{
    var location = OwnedLocation.ResolveFromExecutable(Environment.ProcessPath);
    var currentDirectory = Directory.GetCurrentDirectory();
    return roleName switch
    {
        "codex" => CodexRole.Run(args, location, currentDirectory, OwnedLocation.ReadAllStandardInput),
        "claude" => ClaudeRole.Run(args, location, currentDirectory, OwnedLocation.ReadAllStandardInput),
        "verify" => VerifyRole.Run(args, location, currentDirectory),
        _ => throw new FixtureRefusal(FixtureRefusal.UnsupportedInvocation, "The executable name is not a known fixture role."),
    };
}
catch (FixtureRefusal refusal)
{
    await Console.Error.WriteLineAsync("fixture refused: " + refusal.Message);
    return refusal.ExitCode;
}
