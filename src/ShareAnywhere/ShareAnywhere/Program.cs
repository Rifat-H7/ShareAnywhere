using ShareAnywhere.Services;
using ShareAnywhere.Services.Interfaces;
using ShareAnywhere.Hubs;
var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = null;
});

// Add services to the container
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddDataProtection();
builder.Services.AddSingleton<IFileStoreService, FileStoreService>(); // register service
builder.Services.AddSingleton<PeerRegistry>();

var app = builder.Build();

// Middleware
app.UseStaticFiles();
app.UseRouting();
app.MapHub<NearbyHub>("/nearbyHub");
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=File}/{action=Upload}/{id?}"
);

app.Run();

