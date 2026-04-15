using Microsoft.AspNetCore.Mvc;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;

namespace PMAgent.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AgentController(IAgentExecutor executor) : ControllerBase
{
    [HttpPost("run")]
    [ProducesResponseType(typeof(AgentRunResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AgentRunResult>> Run(
        [FromBody] AgentRunRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Goal))
            return BadRequest("Goal is required.");

        if (request.MaxIterations is < 1 or > 50)
            return BadRequest("MaxIterations must be between 1 and 50.");

        var result = await executor.RunAsync(request, cancellationToken);
        return Ok(result);
    }
}
