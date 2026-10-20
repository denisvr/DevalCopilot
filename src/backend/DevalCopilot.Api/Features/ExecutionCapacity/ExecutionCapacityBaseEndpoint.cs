using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.ExecutionCapacity;

[ApiController]
[Authorize]
[Route("api/execution-capacity")]
public abstract class ExecutionCapacityBaseEndpoint : ControllerBase;
