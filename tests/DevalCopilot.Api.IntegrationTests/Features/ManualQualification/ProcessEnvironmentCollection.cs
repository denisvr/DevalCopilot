using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
    public const string Name = "Process environment (PATH) changes";
}
