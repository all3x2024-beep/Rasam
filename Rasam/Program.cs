using System.Text.Encodings.Web;
using System.Text.Unicode;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Rasam.Data;
using Rasam.Models.Accounting;

var builder = WebApplication.CreateBuilder(args);

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
    options.AccessDeniedPath = "/Accounting/LogIn";
});

// ========================================
// MVC
// ========================================
builder.Services.AddControllersWithViews();

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

using (var scope = app.Services.CreateScope())
{
    var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
    var adminName = builder.Configuration["AdminUserName"];

    var admin = string.IsNullOrWhiteSpace(adminName) ? null : await users.FindByNameAsync(adminName);

    if (admin != null && !await users.IsInRoleAsync(admin, AppRoles.Admin))
        await users.AddToRoleAsync(admin, AppRoles.Admin);
}

// ========================================
// Pipeline
// ========================================
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseMigrationsEndPoint();
}
else
{
    app.UseMigrationsEndPoint();
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