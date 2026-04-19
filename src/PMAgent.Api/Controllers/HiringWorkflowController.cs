using Microsoft.AspNetCore.Mvc;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;

namespace PMAgent.Api.Controllers;

[ApiController]
[Route("api/hiring/sessions")]
public sealed class HiringWorkflowController(IHiringWorkflowService hiringWorkflowService) : ControllerBase
{
    private static readonly string[] SupportedTechnicalRoles = ["DEV", "TEST"];
    private static readonly string[] SupportedSeniorityLevels = ["AUTO", "JUNIOR", "MID", "SENIOR"];

    [HttpPost]
    [ProducesResponseType(typeof(HiringSessionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<HiringSessionResult>> Start(
        [FromBody] HiringSessionStartRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectBrief))
            return BadRequest("ProjectBrief is required.");

        if (string.IsNullOrWhiteSpace(request.JobDescription))
            return BadRequest("JobDescription is required.");

        if (string.IsNullOrWhiteSpace(request.CandidateCv))
            return BadRequest("CandidateCv is required.");

        if (!string.IsNullOrWhiteSpace(request.TechnicalInterviewRole)
            && !SupportedTechnicalRoles.Contains(request.TechnicalInterviewRole, StringComparer.OrdinalIgnoreCase))
        {
            return BadRequest("TechnicalInterviewRole must be DEV or TEST.");
        }

        if (!string.IsNullOrWhiteSpace(request.TargetSeniority)
            && !SupportedSeniorityLevels.Contains(request.TargetSeniority, StringComparer.OrdinalIgnoreCase))
        {
            return BadRequest("TargetSeniority must be AUTO, JUNIOR, MID, or SENIOR.");
        }

        var normalizedRequest = NormalizeHiringRequest(request);
        var result = await hiringWorkflowService.StartAsync(normalizedRequest, cancellationToken);
        return Ok(result);
    }

    private static HiringSessionStartRequest NormalizeHiringRequest(HiringSessionStartRequest request)
    {
        var technicalRole = string.Equals(request.TechnicalInterviewRole, "TEST", StringComparison.OrdinalIgnoreCase)
            ? "DEV"
            : request.TechnicalInterviewRole;

        return request with { TechnicalInterviewRole = technicalRole };
    }

    [HttpGet("{sessionId:guid}")]
    [ProducesResponseType(typeof(HiringSessionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HiringSessionResult>> Get(Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await hiringWorkflowService.GetAsync(sessionId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{sessionId:guid}/notes")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportNotes(Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await hiringWorkflowService.GetAsync(sessionId, cancellationToken);
        if (result is null || string.IsNullOrWhiteSpace(result.NotesDocumentPath) || !System.IO.File.Exists(result.NotesDocumentPath))
            return NotFound();

        var bytes = await System.IO.File.ReadAllBytesAsync(result.NotesDocumentPath, cancellationToken);
        return File(bytes, "text/markdown", Path.GetFileName(result.NotesDocumentPath));
    }

    [HttpPost("{sessionId:guid}/approve-screening")]
    [ProducesResponseType(typeof(HiringSessionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HiringSessionResult>> ApproveScreening(
        Guid sessionId,
        [FromBody] HiringApprovalRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await hiringWorkflowService.ApproveScreeningAsync(sessionId, request, cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{sessionId:guid}/approve-interview")]
    [ProducesResponseType(typeof(HiringSessionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HiringSessionResult>> ApproveInterview(
        Guid sessionId,
        [FromBody] HiringApprovalRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await hiringWorkflowService.ApproveInterviewScheduleAsync(sessionId, request, cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{sessionId:guid}/candidate-response")]
    [ProducesResponseType(typeof(HiringSessionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HiringSessionResult>> CandidateResponse(
        Guid sessionId,
        [FromBody] HiringCandidateResponseRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await hiringWorkflowService.SubmitCandidateResponseAsync(sessionId, request, cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{sessionId:guid}/hint")]
    [ProducesResponseType(typeof(HiringSessionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HiringSessionResult>> RequestHint(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await hiringWorkflowService.RequestHintAsync(sessionId, cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return BadRequest(ex.Message);
        }
    }
}