using Microsoft.AspNetCore.Authentication.Cookies;
using RangeReservationPoc.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Syncfusion license (offline key validation - no call-home, no cloud dependency).
// Put your key in appsettings.json -> "Syncfusion:LicenseKey" or the env var SYNCFUSION_LICENSE_KEY.
// ---------------------------------------------------------------------------
var licenseKey = builder.Configuration["Syncfusion:LicenseKey"]
                 ?? Environment.GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY");
if (!string.IsNullOrWhiteSpace(licenseKey))
{
    Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(licenseKey);
}

builder.Services
    .AddControllersWithViews()
    // Keep PascalCase so JSON field names match the Scheduler field mapping (Id, Subject, StartTime, ...)
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = null;
        o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

// Local cookie authentication with Admin / Customer roles (swap for AD / ADFS / on-prem IdP in production).
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.AccessDeniedPath = "/Account/Denied";
        o.Cookie.Name = "RangeReservationPoc.Auth";
    });
builder.Services.AddAuthorization();

// Application services (in-memory store for the POC - replace with EF Core / existing backend APIs).
builder.Services.AddSingleton<FacilityClock>();
builder.Services.AddSingleton<InMemoryDataStore>();
builder.Services.AddSingleton<IQualificationRegistry, MockQualificationRegistry>();
builder.Services.AddSingleton<ReservationRulesEngine>();
builder.Services.AddSingleton<ReservationService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
