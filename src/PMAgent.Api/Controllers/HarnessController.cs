using Microsoft.AspNetCore.Mvc;
using PMAgent.Application.Abstractions.Harness;
using PMAgent.Application.Models.Harness;

namespace PMAgent.Api.Controllers;

[ApiController]
[Route("api/harness")]
public sealed class HarnessController(IHarnessRunner harnessRunner) : ControllerBase
{
    /// <summary>
    /// Runs all harness scenarios and returns the full report.
    /// The report is also written to <c>harness-reports/</c> on disk.
    /// </summary>
    [HttpPost("run")]
    [ProducesResponseType(typeof(HarnessReport), StatusCodes.Status200OK)]
    public async Task<ActionResult<HarnessReport>> RunAll(CancellationToken cancellationToken)
    {
        var report = await harnessRunner.RunAllAsync(cancellationToken);
        return Ok(report);
    }

    /// <summary>
    /// Runs a single harness scenario by ID and returns its result.
    /// </summary>
    [HttpPost("run/{scenarioId}")]
    [ProducesResponseType(typeof(HarnessScenarioResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HarnessScenarioResult>> RunScenario(
        string scenarioId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await harnessRunner.RunScenarioAsync(scenarioId, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return NotFound(ex.Message);
        }
    }
}
