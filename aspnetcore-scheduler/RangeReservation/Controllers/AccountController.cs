using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RangeReservationPoc.Models;
using RangeReservationPoc.Services;

namespace RangeReservationPoc.Controllers;

/// <summary>
/// POC sign-in: pick a demo persona. In production replace with Windows / AD / ADFS / an on-prem OIDC provider -
/// the rest of the app only relies on the Role claim and the "tier" claim.
/// </summary>
[AllowAnonymous]
public class AccountController : Controller
{
    private readonly InMemoryDataStore _store;
    public AccountController(InMemoryDataStore store) => _store = store;

    [HttpGet]
    public IActionResult Login() { ViewBag.Groups = _store.Groups; return View(_store.Users); }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string userId)
    {
        var user = _store.FindUser(userId);
        if (user is null) return RedirectToAction(nameof(Login));

        // Clear any existing authentication before signing in new user
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Role, user.Role),
        };
        if (_store.FindGroup(user.GroupId) is { } group)
        {
            claims.Add(new Claim("group", group.Id.ToString()));
            claims.Add(new Claim("tier", group.Tier.ToString()));
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
        // Route based on user role
        if (user.Role == Roles.Admin)
        {
            return RedirectToAction("Queue", "Admin");
        }
        else
        {
            return RedirectToAction("Index", "Home");
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    public IActionResult Denied() => Content("Access denied - administrator role required.");
}

public static class UserExtensions
{
    public static AppUser? CurrentUser(this ClaimsPrincipal principal, InMemoryDataStore store) =>
        store.FindUser(principal.FindFirstValue(ClaimTypes.NameIdentifier));
}
