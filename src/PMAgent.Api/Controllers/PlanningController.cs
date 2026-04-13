using Microsoft.AspNetCore.Mvc;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;

namespace PMAgent.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class PlanningController(IAgentPlanner planner) : ControllerBase
{
    [HttpPost("next-actions")]
    [ProducesResponseType(typeof(PlanningResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PlanningResponse>> BuildPlan(
        [FromBody] PlanningRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectName) || string.IsNullOrWhiteSpace(request.Goal))
        {
            return BadRequest("ProjectName and Goal are required.");
        }

        var response = await planner.BuildPlanAsync(request, cancellationToken);
        return Ok(response);
    }
}
