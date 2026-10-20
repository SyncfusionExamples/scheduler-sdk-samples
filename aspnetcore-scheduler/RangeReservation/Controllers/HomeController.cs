using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RangeReservationPoc.Models;
using RangeReservationPoc.Services;

namespace RangeReservationPoc.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly InMemoryDataStore _store;
    private readonly FacilityClock _clock;

    public HomeController(InMemoryDataStore store, FacilityClock clock)
    {
        _store = store;
        _clock = clock;
    }

    public IActionResult Index()
    {
        var user = User.CurrentUser(_store);
        if (user is null) return RedirectToAction("Login", "Account");
        ViewBag.User = user;
        ViewBag.Facilities = _store.Facilities;
        ViewBag.Ranges = _store.Ranges;
        ViewBag.Context = BuildClientContext(user);
        ViewBag.Now = _clock.Now;
        return View();
    }

    /// <summary>Everything the browser needs to mirror server rules in the UI (the server stays authoritative).</summary>
    private object BuildClientContext(AppUser user) => new
    {
        User = new { user.Id, user.Name, user.Role, Tier = user.Tier?.ToString(), IsAdmin = user.Role == Roles.Admin },
        Policy = user.Tier is { } t ? _store.Policy(t) : null,
        Policies = _store.Policies,
        Facilities = _store.Facilities,
        Ranges = _store.Ranges.Select(r => new
        {
            r.Id, r.FacilityId, r.Name, r.RangeType, r.Color, r.MaxParticipants, r.MaxHoursPerBooking,
            r.MaxSlotsPerTransaction, MinimumTier = r.MinimumTier.ToString(), MinimumTierValue = (int)r.MinimumTier,
            r.RequiredQualification, r.AllowedFirearms,
            OpenTime = r.OpenTime.ToString(@"hh\:mm"), CloseTime = r.CloseTime.ToString(@"hh\:mm")
        }),
        Customers = user.Role == Roles.Admin
            ? _store.Users.Where(u => u.Role == Roles.Customer).Select(u => new { u.Id, u.Name, Tier = u.Tier.ToString() })
            : null,
        Now = _clock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
        TimeZone = _clock.TimeZoneId,
        Firearms = new[] { "Handgun", "Rimfire", "Rifle", "Shotgun" }
    };

    public IActionResult Error() => Content("An unexpected error occurred.");
}
