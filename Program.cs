using Accounts.Models;
using Accounts.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
namespace Accounts
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add logging
            builder.Services.AddLogging(logging =>
            {
                logging.AddConsole();
                logging.AddDebug();
            });

            // Add services to the container.
            builder.Services.AddControllersWithViews();
            builder.Services.AddRazorPages();
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            builder.Services.AddDbContext<AccountContext>(options => // تأكد من أن اسم الـ DbContext صحيح
                options.UseSqlServer(connectionString));

            builder.Services.AddIdentity<IdentityUser, IdentityRole>()
                .AddEntityFrameworkStores<AccountContext>() // تأكد من أن اسم الـ DbContext صحيح
                .AddDefaultTokenProviders();

            builder.Services.Configure<IdentityOptions>(options =>
            {
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 6;
            });

            // 💡 --- التعديلات المطلوبة هنا --- 💡

            // 1. تسجيل كلاس الإعدادات لقرائته من appsettings/secrets.json
            builder.Services.Configure<ArcGisSettings>(builder.Configuration.GetSection("ArcGisSettings"));

            // 2. تسجيل ArcGisService مع HttpClient (هذا السطر كافٍ ويقوم بكل شيء)
            builder.Services.AddHttpClient<ArcGisService>();

            // ❌ السطر المكرر الذي يجب حذفه: builder.Services.AddScoped<ArcGisService>();

            // --- نهاية التعديلات ---

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }
            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            // UseAuthentication must come before UseAuthorization
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.MapRazorPages(); // تأكد من وجود هذا السطر إذا كنت تستخدم صفحات Identity

            app.Run();
        }
    }
}