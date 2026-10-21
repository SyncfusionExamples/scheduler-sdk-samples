using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RangeReservationPoc.Models;
using RangeReservationPoc.Services;

namespace RangeReservationPoc.Controllers;

/// <summary>AZGFD Administrator screens.</summary>
[Authorize(Roles = Roles.Admin)]
public class AdminController : Controller
{
    private readonly InMemoryDataStore _store;
    public AdminController(InMemoryDataStore store) => _store = store;

    /// <summary>UC-12 review queue.</summary>
    public IActionResult Queue()
    {
        ViewBag.User = User.CurrentUser(_store);
        return View();
    }

    /// <summary>UC-13 blackout management.</summary>
    public IActionResult Blackouts()
    {
        ViewBag.User = User.CurrentUser(_store);
        var scopes = new List<object> { new { value = "all", text = "All facilities / all ranges" } };
        scopes.AddRange(_store.Facilities.Select(f => new { value = $"f{f.Id}", text = $"{f.Name} (all ranges)" }));
        scopes.AddRange(_store.Ranges.Select(r => new { value = $"r{r.Id}", text = $"{_store.FindFacility(r.FacilityId)!.Name} - {r.Name}" }));
        ViewBag.Scopes = scopes;
        return View();
    }

    /// <summary>UC-14 corporate groups, tiers and specialized range permissions.</summary>
    public IActionResult Groups()
    {
        ViewBag.User = User.CurrentUser(_store);
        ViewBag.Ranges = _store.Ranges;
        ViewBag.Users = _store.Users;
        return View(_store.Groups);
    }

    /// <summary>Tier policies and facility-wide rules (UC-10, UC-11).</summary>
    public IActionResult Policies()
    {
        ViewBag.User = User.CurrentUser(_store);
        ViewBag.Settings = _store.Settings;
        return View(_store.Policies);
    }
}
