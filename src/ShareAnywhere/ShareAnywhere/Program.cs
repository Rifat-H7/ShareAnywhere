using ShareAnywhere.Services;
using ShareAnywhere.Services.Interfaces;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<IFileStoreService, FileStoreService>(); // register service

var app = builder.Build();

// Middleware
app.UseStaticFiles();
app.UseRouting();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=File}/{action=Upload}/{id?}"
);

app.Run();

