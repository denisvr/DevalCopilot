using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.LocalExecutionQueue;

[ApiController]
[Authorize]
[Route("api/local-execution-queue")]
public abstract class LocalExecutionQueueBaseEndpoint : ControllerBase;
