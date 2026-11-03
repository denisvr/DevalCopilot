using DevalCopilot.Api.IntegrationTests.BrowserJourney.Host;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The one composition of lot B's environment: the unchanged production environment with the process admission boundary around the
/// host's process adapter and around every launcher Git command. The process counters live in the same authorization-scoped directory
/// as the two allowances and are consumed durably before each process is created. The owner-approved launch set is supplied here
/// (or, for the offline rehearsals only, approved by the preparation hook before the first launch).
/// </summary>
public static class Profile01Composition
{
    public static (ProcessAdmission Admission, JudgedTargets Targets) CreateAdmission(
        string ledgerDirectory,
        ApprovedLaunchSet? approved = null,
        EntryDeadline? deadline = null)
    {
        var judged = new JudgedTargets(approved);
        var (entries, total) = Profile01Inventory.Create(judged);
        var counters = new AdmissionLedger(ledgerDirectory, InvocationLedger.Authorization, TimeProvider.System);
        return (
            new ProcessAdmission(
                entries,
                total,
                counters,
                judged.Revalidate,
                () => deadline?.IsExpired == true ? "OuterDeadlineExceeded" : null)
            {
                LauncherGit = () => judged.LauncherGitPath,
            },
            judged);
    }

    public static ProductionQualificationEnvironment CreateEnvironment(
        InvocationLedger ledger,
        QualificationLimits limits,
        string temporaryDirectory,
        ApprovedLaunchSet approved,
        Func<string, string, Func<IReadOnlyList<ProviderTargetFact>, TargetJudgement>> createJudge,
        Action<OwnedRootGuard>? prepareProviders = null,
        IProcessTable? processTable = null,
        int? launcherProcessId = null,
        EntryDeadline? deadline = null)
    {
        var (admission, judged) = CreateAdmission(ledger.LedgerDirectory, approved, deadline);
        return new ProductionQualificationEnvironment(
            limits,
            temporaryDirectory,
            createJudge,
            prepareProviders,
            processTable,
            launcherProcessId,
            admission,
            judged);
    }
}
