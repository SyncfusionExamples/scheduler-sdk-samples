using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RangeReservationPoc.Models;
using RangeReservationPoc.Services;

namespace RangeReservationPoc.Controllers;

/// <summary>Admin-only management screens (blackout windows and tier policies).</summary>
[Authorize(Roles = Roles.Admin)]
public class AdminController : Controller
{
    private readonly InMemoryDataStore _store;
    public AdminController(InMemoryDataStore store) => _store = store;

    public IActionResult Blackouts()
    {
        ViewBag.User = User.CurrentUser(_store);
        var scopes = new List<object> { new { value = "all", text = "All facilities / all ranges" } };
        scopes.AddRange(_store.Facilities.Select(f => new { value = $"f{f.Id}", text = $"{f.Name} (all ranges)" }));
        scopes.AddRange(_store.Ranges.Select(r => new { value = $"r{r.Id}", text = $"{_store.Facilities.First(f => f.Id == r.FacilityId).Name} - {r.Name}" }));
        ViewBag.Scopes = scopes;
        return View();
    }

    public IActionResult Policies()
    {
        ViewBag.User = User.CurrentUser(_store);
        return View(_store.Policies);
    }
}
