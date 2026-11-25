using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using ShareAnywhere.Hubs;
using ShareAnywhere.Services;
using ShareAnywhere.Services.Interfaces;
using System.Net;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var trustedProxies = builder.Configuration.GetSection("ShareAnywhere:TrustedProxies").Get<string[]>() ?? [];
if (trustedProxies.Length > 0)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var proxy in trustedProxies)
        {
            options.KnownProxies.Add(IPAddress.Parse(proxy));
        }
    });
}

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IFileStoreService, FileStoreService>();
builder.Services.AddHostedService<ShareCleanupService>();
builder.Services.AddSingleton<PeerRegistry>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("create-share", context => PerIp(context, 30));
    options.AddPolicy("open-share", context => PerIp(context, 120));
});

var app = builder.Build();
if (trustedProxies.Length > 0)
{
    app.UseForwardedHeaders();
}

app.Use(async (context, next) =>
{
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/Uploads"),
    branch => branch.UseStaticFiles());
app.UseRouting();
app.UseRateLimiter();
app.MapHub<NearbyHub>("/nearbyHub").RequireRateLimiting("open-share");
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=File}/{action=Upload}/{id?}");

app.Run();

static RateLimitPartition<string> PerIp(HttpContext context, int permitLimit) =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromMinutes(1)
        });

