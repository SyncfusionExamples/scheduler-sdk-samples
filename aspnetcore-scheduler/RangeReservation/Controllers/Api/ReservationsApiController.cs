using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RangeReservationPoc.Models;
using RangeReservationPoc.Services;

namespace RangeReservationPoc.Controllers.Api;

/// <summary>
/// REST endpoints consumed by the Syncfusion Scheduler. These are deliberately thin so the same contract can
/// sit in front of the customer's existing reservation services.
/// All date-times are facility wall-clock (America/Phoenix) in "yyyy-MM-ddTHH:mm:ss" form.
/// </summary>
[ApiController]
[Authorize]
[Route("api")]
public class ReservationsApiController : ControllerBase
{
    private readonly InMemoryDataStore _store;
    private readonly ReservationService _service;

    public ReservationsApiController(InMemoryDataStore store, ReservationService service)
    {
        _store = store;
        _service = service;
    }

    /// <summary>Events for the visible date range (reservations + blackout blocks). Customers get masked data.</summary>
    [HttpGet("schedule/events")]
    public ActionResult<List<ScheduleEventDto>> GetEvents([FromQuery] DateTime? start, [FromQuery] DateTime? end)
    {
        var user = User.CurrentUser(_store);
        if (user is null) return Unauthorized();
        return _service.GetEvents(user, start, end);
    }

    /// <summary>
    /// Single transactional endpoint for create / update / delete (same added/changed/deleted shape the
    /// Scheduler produces). Returns 422 with every rule violation when the transaction is rejected.
    /// </summary>
    [HttpPost("reservations/batch")]
    public ActionResult<BatchResult> Batch([FromBody] ReservationBatchRequest request)
    {
        var user = User.CurrentUser(_store);
        if (user is null) return Unauthorized();
        var result = _service.ApplyBatch(user, request);
        return result.Success ? Ok(result) : UnprocessableEntity(result);
    }

    [HttpGet("policies")]
    public ActionResult<List<TierPolicy>> GetPolicies() => _service.GetPolicies();

    [HttpPut("policies")]
    [Authorize(Roles = Roles.Admin)]
    public IActionResult UpdatePolicies([FromBody] List<TierPolicy> policies)
    {
        var errors = _service.UpdatePolicies(policies);
        return errors.Count == 0 ? Ok(_service.GetPolicies()) : UnprocessableEntity(new { Errors = errors });
    }

    [HttpGet("blackouts")]
    [Authorize(Roles = Roles.Admin)]
    public ActionResult<List<Blackout>> GetBlackouts() => _service.GetBlackouts();

    [HttpPost("blackouts")]
    [Authorize(Roles = Roles.Admin)]
    public IActionResult SaveBlackout([FromBody] BlackoutInput input)
    {
        var (saved, errors, impacted) = _service.SaveBlackout(input);
        return errors.Count == 0 ? Ok(new { Blackout = saved, Impacted = impacted }) : UnprocessableEntity(new { Errors = errors });
    }

    [HttpDelete("blackouts/{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public IActionResult DeleteBlackout(int id) => _service.DeleteBlackout(id) ? NoContent() : NotFound();
}
