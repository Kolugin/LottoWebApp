//Program.cs
using LottoWebApp.Data;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// SERVICES CONFIGURATION
// Database Context
builder.Services.AddDbContext<LottoDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("LottoDataDb")));

// Core Services
builder.Services.AddHttpClient();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ICacheService, MemoryCacheService>();
builder.Services.AddScoped<AppServices>();
builder.Services.AddScoped<LotteryQueryProvider>();

//Загрузка лотерей с сайта
builder.Services.AddScoped<LotteryDataDownloader>();

builder.Services.AddHostedService<BlitzDownloadService>();
builder.Services.AddHostedService<KenoDownloadService>();
builder.Services.AddHostedService<_536DownloadService>();
builder.Services.AddHostedService<_649DownloadService>();
builder.Services.AddHostedService<_1224DownloadService>();
builder.Services.AddHostedService<_320DownloadService>();

// Session Configuration
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Authentication Configuration
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.AccessDeniedPath = "/Auth/AccessDenied";
        options.LogoutPath = "/Auth/Logout";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

    });

builder.Services.AddAuthorization();

// Razor Pages
builder.Services.AddRazorPages();
// CSV helpers …
builder.Services.AddScoped<A11CsvExportHelper>()
                .AddScoped<A12CsvExportHelper>()
                .AddScoped<A13CsvExportHelper>()
                .AddScoped<A14CsvExportHelper>()
                .AddScoped<A15CsvExportHelper>()
                .AddScoped<A16CsvExportHelper>()
                .AddScoped<A17CsvExportHelper>()
                .AddScoped<A18CsvExportHelper>()
                .AddScoped<A19CsvExportHelper>()
                .AddScoped<A110CsvExportHelper>()
                .AddScoped<A111CsvExportHelper>()
                .AddScoped<A112CsvExportHelper>()
                .AddScoped<A113CsvExportHelper>()
                .AddScoped<A114CsvExportHelper>()
                .AddScoped<A115CsvExportHelper>()
                .AddScoped<A116CsvExportHelper>()
                .AddScoped<A117CsvExportHelper>()
                .AddScoped<A118CsvExportHelper>()
                .AddScoped<A21CsvExportHelper>()
                .AddScoped<A22CsvExportHelper>()
                .AddScoped<A23CsvExportHelper>()
                .AddScoped<A31CsvExportHelper>()
                .AddScoped<A32CsvExportHelper>()
                .AddScoped<A33CsvExportHelper>()
                .AddScoped<A34CsvExportHelper>()
                .AddScoped<A35CsvExportHelper>()
                .AddScoped<A36CsvExportHelper>()
                .AddScoped<A37CsvExportHelper>()
                .AddScoped<A38CsvExportHelper>()
                .AddScoped<A39CsvExportHelper>()
                .AddScoped<A310CsvExportHelper>()
                .AddScoped<A311CsvExportHelper>()
                .AddScoped<A312CsvExportHelper>()
                .AddScoped<A313CsvExportHelper>()
                .AddScoped<A314CsvExportHelper>()
                .AddScoped<A315CsvExportHelper>()
                .AddScoped<A316CsvExportHelper>()
                .AddScoped<A317CsvExportHelper>()
                .AddScoped<A318CsvExportHelper>()
                .AddScoped<A319CsvExportHelper>()
                .AddScoped<A320CsvExportHelper>()
                .AddScoped<A321CsvExportHelper>()
                .AddScoped<A41CsvExportHelper>()
                .AddScoped<A42CsvExportHelper>()
                .AddScoped<A51CsvExportHelper>()
                .AddScoped<A52CsvExportHelper>()
                .AddScoped<A53CsvExportHelper>()
                .AddScoped<A54CsvExportHelper>()
                .AddScoped<A61CsvExportHelper>()
                .AddScoped<A62CsvExportHelper>()
                .AddScoped<A63CsvExportHelper>()
                .AddScoped<A64CsvExportHelper>()
                .AddScoped<A65CsvExportHelper>();

// Kestrel Ports
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5000);
    options.ListenAnyIP(5001, listenOptions => listenOptions.UseHttps());
    options.ListenAnyIP(6000);
    options.ListenAnyIP(6001, listenOptions => listenOptions.UseHttps());
});

var app = builder.Build();

// MIDDLEWARE

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// ADMIN PORT CONTROL
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    var port = context.Request.Host.Port ?? 0;

    bool isAdminPort = port is 6000 or 6001;
    bool isAdminPath = path.StartsWithSegments("/Auth/LoginAdmin") ||
                        path.StartsWithSegments("/Auth/RegisterAdmin") ||
                        path.StartsWithSegments("/Admin");

    //  Проверка доступа с пользовательского порта
    if (isAdminPath && !isAdminPort)
    {
        context.Response.StatusCode = 403;
        await context.Response.WriteAsync("Доступ к админке запрещён с пользовательского порта.");
        return;
    }

    if (isAdminPort)
    {
        bool isLoginOrRegister =
            path.StartsWithSegments("/Auth/LoginAdmin") ||
            path.StartsWithSegments("/Auth/RegisterAdmin");

        // 1. Проверка неаутентифицированных пользователей 
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            if (!isLoginOrRegister)
            {
                context.Response.Redirect("/Auth/LoginAdmin");
                return;
            }
        }
        // 2. Проверка аутентифицированных пользователей
        else 
        {
            
            bool isProtectedAdminPage = isAdminPath && !isLoginOrRegister;

            if (isProtectedAdminPage && !context.User.IsInRole("Admin"))
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsync("Доступ запрещён. Недостаточно прав администратора.");
                return;
            }
        }
    }

    await next();
});

app.MapRazorPages();

app.Run();
