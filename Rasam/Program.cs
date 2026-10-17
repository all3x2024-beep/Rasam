using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Rasam.Data;
using Rasam.Models.Accounting;

var builder = WebApplication.CreateBuilder(args);

// ========================================
// Request Entity Too Large
// ========================================
builder.Services.Configure<FormOptions>(options =>
{
    // 500 MB
    options.MultipartBodyLengthLimit = 524288000;
});
// ========================================
// فارسی در HTML / JavaScript
// ========================================
builder.Services.AddSingleton<HtmlEncoder>(
    HtmlEncoder.Create(
        allowedRanges: new[]
        {
            UnicodeRanges.BasicLatin,
            UnicodeRanges.Arabic
        }
    )
);

// ========================================
// Database
// ========================================
var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' not found."
    );

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// ========================================
// Identity
// ========================================
builder.Services.AddIdentity<User, Role>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.SignIn.RequireConfirmedPhoneNumber = false;
    options.SignIn.RequireConfirmedEmail = true;
    options.User.AllowedUserNameCharacters =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
    options.User.RequireUniqueEmail = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultUI()
.AddDefaultTokenProviders();

// ========================================
// Session
// ========================================
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(20);
});

// ========================================
// Cookie
// ========================================
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
    options.Cookie.MaxAge = TimeSpan.FromMinutes(60);
    options.SlidingExpiration = true;

    options.LoginPath = "/Accounting/LogIn";
    options.AccessDeniedPath = "/Accounting/AccessDenied";

    // درخواست‌های fetch/AJAX به‌جای Redirect به صفحهٔ HTML، کد 401/403 بگیرند
    static bool IsAjax(HttpRequest r) =>
        r.Headers.XRequestedWith == "XMLHttpRequest" ||
        (r.Headers.TryGetValue("Sec-Fetch-Mode", out var mode) && mode != "navigate") ||
        r.Headers.Accept.ToString().Contains("application/json");

    options.Events.OnRedirectToLogin = ctx =>
    {
        if (IsAjax(ctx.Request)) ctx.Response.StatusCode = 401;
        else ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };

    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        if (IsAjax(ctx.Request)) ctx.Response.StatusCode = 403;
        else ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
});

// ========================================
// MVC
// ========================================
builder.Services.AddControllersWithViews();

// ========================================
// Authorization (مجوزهای قابل تیک‌زدن برای مشاهده‌گر)
// ========================================
builder.Services.AddAuthorization(options =>
{
    foreach (var p in Permissions.All)
    {
        options.AddPolicy(p.Key, policy => policy.RequireAssertion(ctx =>
            ctx.User.IsInRole(AppRoles.Admin) ||
            ctx.User.IsInRole(AppRoles.Editor) ||
            ctx.User.HasClaim(Permissions.ClaimType, p.Key)));
    }
});




// ========================================
// IIS Upload Limit
// ========================================
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = 2147483648;
});


// ========================================
// Build
// ========================================
var app = builder.Build();


// ========================================
// ساخت نقش‌ها و تعیین مدیر اول
// ========================================
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        // اگر نقش‌ها قبلاً (مثلاً با Migration) ساخته شده باشند، کاری انجام نمی‌شود
        foreach (var roleName in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
                await roleManager.CreateAsync(new Role { Name = roleName });
        }

        // نام کاربری مدیر از appsettings.json خوانده می‌شود: "AdminUserName": "Mostafa"
        var adminName = builder.Configuration["Mostafa"];

        var admin = string.IsNullOrWhiteSpace(adminName)
            ? null
            : await users.FindByNameAsync(adminName);

        if (admin != null && !await users.IsInRoleAsync(admin, AppRoles.Admin))
            await users.AddToRoleAsync(admin, AppRoles.Admin);
    }
    catch (Exception ex)
    {
        // مثلاً وقتی دیتابیس هنوز Update نشده است؛ برنامه بالا می‌آید ولی نقش‌ها ساخته نمی‌شوند
        logger.LogError(ex, "Seeding roles failed.");
    }
}


if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseMigrationsEndPoint();   // فقط در توسعه؛ در Production نباید فعال باشد
}



app.UseHttpsRedirection();

// مهم: فایل‌های wwwroot را سرو می‌کند
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseSession();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Accounting}/{action=LogIn}/{id?}");

app.Run();