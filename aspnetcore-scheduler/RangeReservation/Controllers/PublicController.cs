using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RangeReservationPoc.Controllers;
using RangeReservationPoc.Services;

namespace RangeReservationPoc.Controllers;

/// <summary>UC-13: external (public, read-only) availability calendar - shows blackouts and taken slots, no details.</summary>
[AllowAnonymous]
public class PublicController : Controller
{
    private readonly InMemoryDataStore _store;
    private readonly FacilityClock _clock;

    public PublicController(InMemoryDataStore store, FacilityClock clock)
    {
        _store = store;
        _clock = clock;
    }

    public IActionResult Calendar()
    {
        ViewBag.User = User.Identity?.IsAuthenticated == true ? User.CurrentUser(_store) : null;
        ViewBag.Now = _clock.Now;
        ViewBag.Facilities = _store.Facilities;
        ViewBag.Ranges = _store.Ranges;
        return View();
    }
}
