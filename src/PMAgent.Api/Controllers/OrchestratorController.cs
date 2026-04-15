using Microsoft.AspNetCore.Mvc;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;

namespace PMAgent.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class OrchestratorController(IOrchestratorAgent orchestrator) : ControllerBase
{
    [HttpPost("run")]
    [ProducesResponseType(typeof(OrchestrationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrchestrationResult>> Run(
        [FromBody] OrchestrationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectBrief))
            return BadRequest("ProjectBrief is required.");

        if (request.MaxIterationsPerAgent is < 1 or > 50)
            return BadRequest("MaxIterationsPerAgent must be between 1 and 50.");

        var result = await orchestrator.RunAsync(request, cancellationToken);
        return Ok(result);
    }
}
