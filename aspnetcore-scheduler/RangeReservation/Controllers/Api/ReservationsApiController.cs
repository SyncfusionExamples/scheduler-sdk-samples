using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RangeReservationPoc.Models;
using RangeReservationPoc.Services;

namespace RangeReservationPoc.Controllers.Api;

/// <summary>
/// REST endpoints consumed by the Syncfusion Scheduler and the admin screens. Deliberately thin so the same contract
/// can sit in front of the customer's existing reservation services.
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

    private AppUser? Me => User.CurrentUser(_store);

    // ---------------- Calendar ----------------
    [HttpGet("schedule/events")]
    public ActionResult<List<ScheduleEventDto>> GetEvents() => Me is { } me ? _service.GetEvents(me) : Unauthorized();

    /// <summary>UC-06 / 07 / 09: create, edit or cancel - one all-or-nothing transaction. 422 lists every violation.</summary>
    [HttpPost("reservations/batch")]
    public ActionResult<BatchResult> Batch([FromBody] ReservationBatchRequest request)
    {
        if (Me is not { } me) return Unauthorized();
        var result = _service.ApplyBatch(me, request);
        return result.Success ? Ok(result) : UnprocessableEntity(result);
    }

    // ---------------- Requests (dashboard + UC-12 queue) ----------------
    [HttpGet("requests")]
    public ActionResult<List<RequestSummary>> Requests([FromQuery] ReservationStatus? status)
        => Me is { } me ? _service.GetRequests(me, status) : Unauthorized();

    [HttpPost("requests/{requestId}/cancel")]
    public IActionResult Cancel(string requestId)
    {
        if (Me is not { } me) return Unauthorized();
        var (ok, errors) = _service.CancelRequest(requestId, me);
        return ok ? NoContent() : UnprocessableEntity(new { Errors = errors });
    }

    [HttpPost("requests/{requestId}/approve")]
    [Authorize(Roles = Roles.Admin)]
    public IActionResult Approve(string requestId)
    {
        var (ok, errors) = _service.Approve(requestId, Me!);
        return ok ? NoContent() : UnprocessableEntity(new { Errors = errors });
    }

    [HttpPost("requests/{requestId}/deny")]
    [Authorize(Roles = Roles.Admin)]
    public IActionResult Deny(string requestId, [FromBody] DenyInput input)
    {
        var (ok, errors) = _service.Deny(requestId, input.Reason, Me!);
        return ok ? NoContent() : UnprocessableEntity(new { Errors = errors });
    }

    [HttpGet("notifications")]
    public ActionResult<List<NotificationRecord>> Notifications() => Me is { } me ? _service.GetNotifications(me) : Unauthorized();

    // ---------------- UC-13 blackouts ----------------
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

    // ---------------- UC-14 groups & policies ----------------
    [HttpPut("groups/{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public IActionResult UpdateGroup(int id, [FromBody] GroupUpdateInput input)
    {
        var (ok, errors) = _service.UpdateGroup(id, input, Me!);
        return ok ? NoContent() : UnprocessableEntity(new { Errors = errors });
    }

    [HttpPut("settings")]
    [Authorize(Roles = Roles.Admin)]
    public IActionResult UpdateSettings([FromBody] SettingsInput input)
    {
        var errors = _service.UpdateSettings(input);
        return errors.Count == 0 ? NoContent() : UnprocessableEntity(new { Errors = errors });
    }

    // ---------------- Public (external calendars) ----------------
    [HttpGet("public/events")]
    [AllowAnonymous]
    public ActionResult<List<ScheduleEventDto>> PublicEvents() => _service.GetEvents(null);
}
