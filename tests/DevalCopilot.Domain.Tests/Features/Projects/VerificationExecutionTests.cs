using DevalCopilot.Domain.Features.Projects;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Projects;

public sealed class VerificationExecutionTests
{
    [Fact]
    public void Claim_snapshots_the_recipe_and_marks_a_zero_exit_against_the_same_checkpoint_as_passed()
    {
        var project = Project.Register(Guid.NewGuid(), "Project", @"C:\repos\Project", DateTimeOffset.UtcNow);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, @"C:\workspaces\Project", "devalcopilot/workspace/1/1",
            new string('a', 40), "main", DateTimeOffset.UtcNow);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, 1, DateTimeOffset.UtcNow, new string('a', 40), new string('b', 64), []);
        var arguments = new List<string> { "test" };
        var command = VerificationCommand.Configure(
            Guid.NewGuid(), project.Id, 1, "Tests", @"C:\tools\dotnet.exe", arguments, 120, true, DateTimeOffset.UtcNow);

        var execution = VerificationExecution.Claim(
            Guid.NewGuid(), project.Id, project.ReserveVerificationExecutionNumber(), workspace, checkpoint, command, DateTimeOffset.UtcNow);
        arguments.Add("--tampered");
        execution.MarkDispatched(DateTimeOffset.UtcNow);
        execution.Complete(VerificationExecutionOutcome.Exited, 0, checkpoint.FingerprintSha256, DateTimeOffset.UtcNow);

        Assert.Equal(VerificationExecutionStatus.Passed, execution.Status);
        Assert.Equal(["test"], execution.Arguments);
        Assert.Equal(checkpoint.FingerprintSha256, execution.CompletionFingerprintSha256);
    }

    [Fact]
    public void Complete_marks_source_changed_even_when_the_process_succeeds()
    {
        var project = Project.Register(Guid.NewGuid(), "Project", @"C:\repos\Project", DateTimeOffset.UtcNow);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, @"C:\workspaces\Project", "devalcopilot/workspace/1/1",
            new string('a', 40), "main", DateTimeOffset.UtcNow);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, 1, DateTimeOffset.UtcNow, new string('a', 40), new string('b', 64), []);
        var command = VerificationCommand.Configure(
            Guid.NewGuid(), project.Id, 1, "Tests", @"C:\tools\dotnet.exe", [], 120, true, DateTimeOffset.UtcNow);
        var execution = VerificationExecution.Claim(
            Guid.NewGuid(), project.Id, 1, workspace, checkpoint, command, DateTimeOffset.UtcNow);

        execution.MarkDispatched(DateTimeOffset.UtcNow);
        execution.Complete(VerificationExecutionOutcome.Exited, 0, new string('c', 64), DateTimeOffset.UtcNow);

        Assert.Equal(VerificationExecutionStatus.SourceChanged, execution.Status);
    }

    [Fact]
    public void Complete_rejects_an_exit_code_for_a_non_exited_process()
    {
        var project = Project.Register(Guid.NewGuid(), "Project", @"C:\repos\Project", DateTimeOffset.UtcNow);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, @"C:\workspaces\Project", "devalcopilot/workspace/1/1",
            new string('a', 40), "main", DateTimeOffset.UtcNow);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, 1, DateTimeOffset.UtcNow, new string('a', 40), new string('b', 64), []);
        var command = VerificationCommand.Configure(
            Guid.NewGuid(), project.Id, 1, "Tests", @"C:\tools\dotnet.exe", [], 120, true, DateTimeOffset.UtcNow);
        var execution = VerificationExecution.Claim(
            Guid.NewGuid(), project.Id, 1, workspace, checkpoint, command, DateTimeOffset.UtcNow);

        execution.MarkDispatched(DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(
            () => execution.Complete(VerificationExecutionOutcome.TimedOut, 1, checkpoint.FingerprintSha256, DateTimeOffset.UtcNow));
    }
}
