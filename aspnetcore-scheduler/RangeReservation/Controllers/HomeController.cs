using System.Globalization;
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

    /// <summary>Reservation calendar (UC-06 / 07 / 09).</summary>
    public IActionResult Index()
    {
        var user = User.CurrentUser(_store);
        if (user is null) return RedirectToAction("Login", "Account");
        var group = _store.FindGroup(user.GroupId);
        ViewBag.User = user;
        ViewBag.Group = group;
        ViewBag.Now = _clock.Now;
        // UC-14: a group only sees standard ranges it is eligible for plus the specialized ranges it has been granted.
        var visible = _store.Ranges.Where(r => user.Role == Roles.Admin
            || (_store.IsRangeAuthorized(group, r) && (group is null || r.MinimumTier <= group.Tier))).ToList();
        ViewBag.Ranges = visible;
        ViewBag.Facilities = _store.Facilities.Where(f => visible.Any(r => r.FacilityId == f.Id)).ToList();
        ViewBag.Context = BuildClientContext(user, group, visible);
        return View();
    }

    /// <summary>UC-12: the group's dashboard - request status, denial reasons and email notices.</summary>
    public IActionResult MyReservations()
    {
        var user = User.CurrentUser(_store);
        if (user is null) return RedirectToAction("Login", "Account");
        if (user.Role == Roles.Admin) return RedirectToAction("Queue", "Admin");
        ViewBag.User = user;
        ViewBag.Group = _store.FindGroup(user.GroupId);
        return View();
    }

    private object BuildClientContext(AppUser user, CorporateGroup? group, List<ShootingRange> visible) => new
    {
        User = new
        {
            user.Id, user.Name, user.Role, IsAdmin = user.Role == Roles.Admin, user.GroupId,
            GroupName = group?.Name, GroupRole = user.GroupRole?.ToString(),
            Tier = group?.Tier.ToString(), TierName = group is null ? null : TierNames.Display(group.Tier)
        },
        Policy = group is null ? null : _store.Policy(group.Tier),
        Settings = _store.Settings,
        // Machine-readable dates for scheduler.js: always invariant culture. Some server cultures (e.g. en-DK, fi, da)
        // use "." as the time separator or a non-Gregorian calendar, which the client could not parse.
        RecurrenceCap = ReservationRulesEngine.RecurrenceCap(_clock.Now).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        SingleRangeMessage = ReservationRulesEngine.SingleRangeMessage,
        Facilities = _store.Facilities,
        Ranges = visible.Select(r => new
        {
            r.Id, r.FacilityId, r.Name, r.RangeType, r.Color, r.MaxHeadcount, r.MaxHoursPerBooking,
            MinimumTier = r.MinimumTier.ToString(), MinimumTierName = TierNames.Display(r.MinimumTier), MinimumTierValue = (int)r.MinimumTier,
            r.IsSpecialized, r.SpecializedRequirement, r.AllowedFirearms, r.AllowedTargetTypes, r.MaxTargetDistanceYards,
            OpenTime = r.OpenTime.ToString(@"hh\:mm"), CloseTime = r.CloseTime.ToString(@"hh\:mm")
        }),
        Groups = user.Role == Roles.Admin
            ? _store.Groups.Select(g => new { g.Id, g.Name, Tier = g.Tier.ToString(), TierName = TierNames.Display(g.Tier), Permissions = g.RangePermissions.Select(p => p.RangeId) })
            : null,
        Now = _clock.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        TimeZone = _clock.TimeZoneId,
        Firearms = new[] { "Handgun", "Rimfire", "Rifle", "Shotgun", "Muzzleloader" },
        TargetTypes = new[] { "Paper", "Steel", "Clay", "Reactive", "Silhouette" }
    };

    public IActionResult Error() => Content("An unexpected error occurred.");
}
