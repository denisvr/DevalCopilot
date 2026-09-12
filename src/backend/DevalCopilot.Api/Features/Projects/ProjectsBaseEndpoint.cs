using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects;

[ApiController]
[Authorize]
[Route("api/projects")]
public abstract class ProjectsBaseEndpoint : ControllerBase;
