using Microsoft.EntityFrameworkCore;
using EmployeeSystem.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using EmployeeSystem.Services;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

var mvcBuilder = builder.Services.AddControllersWithViews();
if (builder.Environment.IsDevelopment())
{
    mvcBuilder.AddRazorRuntimeCompilation();
}
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IEmployeeAnalyticsService, EmployeeAnalyticsService>();
builder.Services.AddScoped<IAiSqlAgentService, AiSqlAgentService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<AuditInterceptor>();

// Background task queue and email services
builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
builder.Services.AddHostedService<QueuedHostedService>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<INotificationService, NotificationService>();


builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

builder.Services.AddAuthorization();

builder.Services.AddDbContext<EmployeeDbContext>((sp, options) =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ??
        builder.Configuration["DATABASE_URL"];
    var databaseProvider = builder.Configuration["DatabaseProvider"];
    var usePostgres = string.Equals(databaseProvider, "Postgres", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(databaseProvider, "PostgreSQL", StringComparison.OrdinalIgnoreCase);

    if (usePostgres)
    {
        options.UseNpgsql(NormalizePostgresConnectionString(connectionString));
    }
    else
    {
        options.UseSqlServer(connectionString);
    }

    options.AddInterceptors(sp.GetRequiredService<AuditInterceptor>());
});

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services, app.Configuration);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.Run();

static string? NormalizePostgresConnectionString(string? connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString) ||
        (!connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
         !connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)))
    {
        return connectionString;
    }

    var uri = new Uri(connectionString);
    var userInfo = uri.UserInfo.Split(':', 2);

    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
        Username = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(0) ?? string.Empty),
        Password = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(1) ?? string.Empty),
        Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
        SslMode = SslMode.Require
    };

    return builder.ConnectionString;
}
