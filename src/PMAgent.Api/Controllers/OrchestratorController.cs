using Microsoft.AspNetCore.Mvc;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;

namespace PMAgent.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class OrchestratorController(IOrchestratorAgent orchestrator) : ControllerBase
{
    private static readonly string[] SupportedWorkflows = ["delivery", "hiring"];
    private static readonly string[] SupportedTechnicalRoles = ["DEV", "TEST"];

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

        if (!SupportedWorkflows.Contains(request.Workflow, StringComparer.OrdinalIgnoreCase))
            return BadRequest("Workflow must be either 'delivery' or 'hiring'.");

        if (string.Equals(request.Workflow, "hiring", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.JobDescription))
                return BadRequest("JobDescription is required when Workflow is 'hiring'.");

            if (string.IsNullOrWhiteSpace(request.CandidateCv))
                return BadRequest("CandidateCv is required when Workflow is 'hiring'.");
        }

        if (request.TechnicalInterviewRoles is not null
            && request.TechnicalInterviewRoles.Any(role => !SupportedTechnicalRoles.Contains(role, StringComparer.OrdinalIgnoreCase)))
        {
            return BadRequest("TechnicalInterviewRoles only supports DEV and TEST.");
        }

        var result = await orchestrator.RunAsync(request, cancellationToken);
        return Ok(result);
    }
}
