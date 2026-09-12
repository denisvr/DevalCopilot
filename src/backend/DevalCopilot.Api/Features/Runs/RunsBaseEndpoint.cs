using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs;

[ApiController]
[Authorize]
[Route("api/runs")]
public abstract class RunsBaseEndpoint : ControllerBase;
