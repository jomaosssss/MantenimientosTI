using MantenimientosTI.Models;
using MantenimientosTI.Services;
using MantenimientosTI.Services.ImageValidation;
using MantenimientosTI.Services.ImageValidation.Interfaces;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<MantenimientosTIContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("ConexionSQL")));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();

// Servicios personalizados
//builder.Services.AddScoped<MantenimientosTI.Services.BitacoraService>();

builder.Services.AddScoped<MantenimientosTI.Services.PlantillaBitacoraService>();
builder.Services.AddScoped<InformacionSistema>();
builder.Services.AddScoped<ReporteService>();
builder.Services.AddScoped<IPerceptualHashService, PerceptualHashService>();
builder.Services.AddScoped<IImageValidator, BasicImageValidator>();
builder.Services.AddScoped<BitacoraService>();
builder.Services.AddHostedService<ScheduledEmailService>();

// Configuración de Data Protection
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(@"C:\temp-keys\"))
    .SetApplicationName("MantenimientosTI")
    .SetDefaultKeyLifetime(TimeSpan.FromDays(90));

// Configuración de sesión
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// Configuración de autenticación con cookies
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Usuario/Login";
        options.AccessDeniedPath = "/Home/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = true;
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads")),
    RequestPath = "/uploads"
});

app.UseHttpsRedirection();
app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Usuario}/{action=Login}/{id?}");

app.Run();