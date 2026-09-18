using Microsoft.EntityFrameworkCore;
using SNP_SubSystem.Middleware;
using Sub_Entities.Entities;
using Sub_Services.Execute;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<SNP_SubSystemDBContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")
    ));

// Dang ky SubSystemService vao DI container
builder.Services.AddScoped<SubSystemService>();

// Dang ky Backup service
builder.Services.AddSingleton<SNP_SubSystem.Services.BackupService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SNP_SubSystem.Services.BackupService>());

builder.Services.AddControllersWithViews();

builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.LogoutPath = "/Login/Logout";
        options.ExpireTimeSpan = TimeSpan.FromHours(8); // mặc định 8h
        options.SlidingExpiration = true; // gia hạn khi người dùng thao tác
        options.Cookie.Name = PermissionMiddleware.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Events.OnRedirectToLogin = ctx =>
        {
            bool isAjax = ctx.Request.Headers["X-Requested-With"] == "XMLHttpRequest"
                          || (!ctx.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase));
            if (isAjax)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            }
            else
            {
                ctx.Response.Redirect(ctx.RedirectUri);
            }
            return Task.CompletedTask;
        };
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<PermissionMiddleware>();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// ── Seed tài khoản admin mặc định ─────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SNP_SubSystemDBContext>();
    if (!db.Users.Any(u => u.Username == "admin"))
    {
        // Tạo salt + hash mật khẩu theo cùng cơ chế SHA256(password + salt)
        var salt = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLower();
        var rawPwd = "MnHoai@123";
        var combined = rawPwd + salt;
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(combined));
        var hash = Convert.ToHexString(bytes).ToLower();

        db.Users.Add(new Sub_Entities.Entities.User
        {
            Id           = Guid.NewGuid(),
            Username     = "admin",
            FullName     = "Administrator",
            Msnv         = "ADMIN",
            Email        = "admin@snp.local",
            HashCode     = salt,
            PasswordHash = hash,
            Keyword      = string.Empty,
            Status       = 1,
            ExpiryDate   = DateTime.Now.AddYears(99),
            CreateDate   = DateTime.Now,
            UpdateDate   = DateTime.Now,
        });
        db.SaveChanges();
        Console.WriteLine("[Seed] Tài khoản admin đã được tạo.");
    }
}

app.Run();
