using DevalCopilot.Api.IntegrationTests.BrowserJourney.Host;
using DevalCopilot.Domain.Features.EnvironmentReadiness;

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

    /// <summary>Composes lot B only from the owner's approved-launch manifest, inside the entry's bounded preparation: its absence, a
    /// structural problem or any listed file that is not exactly the approved one refuses first. Then every capability is resolved by
    /// the production target resolver in this process's own environment and compared with the manifest, and any missing, extra,
    /// ambiguous or different launch refuses. All of it happens before a host, a directory, a ledger entry, a Git probe or a process
    /// exists, and the entry's deadline is asked between every step.</summary>
    public static ProductionQualificationEnvironment CreateFromManifest(
        string? manifestPath,
        InvocationLedger ledger,
        string temporaryDirectory,
        EntryDeadline deadline,
        Func<Func<Capability, LaunchResolution>?> bindResolver)
    {
        var path = manifestPath ?? throw new EnvironmentRefusedException("ApprovedLaunchesRequired");
        var (approved, refusal) = ApprovedLaunchManifest.Load(path);
        if (approved is null)
        {
            throw new EnvironmentRefusedException(refusal ?? "ApprovedLaunchesInvalid");
        }

        if (deadline.IsExpired)
        {
            throw new OperationCanceledException("The entry deadline elapsed.");
        }

        var resolve = bindResolver() ?? throw new EnvironmentRefusedException(LaunchContextCheck.ResolverUnavailableCode);
        if (LaunchContextCheck.Refusal(approved, resolve, () => deadline.IsExpired) is { } mismatch)
        {
            throw new EnvironmentRefusedException(mismatch);
        }

        return CreateEnvironment(
            ledger,
            QualificationLimits.Production,
            temporaryDirectory,
            approved,
            (root, launcherDirectory) => facts => new LaunchTargetJudge(root, launcherDirectory).Judge(facts),
            deadline: deadline);
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
