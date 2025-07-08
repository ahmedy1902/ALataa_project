using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Accounts.ViewModels;
using Microsoft.AspNetCore.Authorization;
using System.Linq;
using System.Collections.Generic;
using Accounts.Models;
using Accounts.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;

public class AccountController : Controller
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly AccountContext _context;
    private readonly ArcGisService _arcGisService;
    private readonly ILogger<AccountController> _logger;
    private readonly ArcGisSettings _arcGisSettings;

    public AccountController(UserManager<IdentityUser> userManager,
                             SignInManager<IdentityUser> signInManager,
                             RoleManager<IdentityRole> roleManager,
                             AccountContext context,
                             ArcGisService arcGisService,
                             ILogger<AccountController> logger,
                             IOptions<ArcGisSettings> arcGisSettings)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _context = context;
        _arcGisService = arcGisService;
        _logger = logger;
        _arcGisSettings = arcGisSettings.Value;
    }

    // ✅ Register - GET
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Register() => View();

    // ✅ Register - POST
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Register(RegisterModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = new IdentityUser { UserName = model.Email, Email = model.Email };
        var result = await _userManager.CreateAsync(user, model.Password);

        if (result.Succeeded)
        {
            if (!string.IsNullOrEmpty(model.Role))
            {
                if (!await _roleManager.RoleExistsAsync(model.Role))
                    await _roleManager.CreateAsync(new IdentityRole(model.Role));

                await _userManager.AddToRoleAsync(user, model.Role);
            }

            // 💡 --- تم استدعاء الخدمة هنا بدلاً من الكود المعقد ---
            bool arcGisSuccess = true; // Assume success for roles without ArcGIS data
            if (model.Role == "Charity")
            {
                arcGisSuccess = await _arcGisService.SendCharityDataAsync(model);
            }
            else if (model.Role == "Donor")
            {
                arcGisSuccess = await _arcGisService.SendDonorDataAsync(model);
            }

            if (!arcGisSuccess)
            {
                _logger.LogWarning("User '{email}' was created, but failed to submit profile data to ArcGIS.", model.Email);
                // يمكنك إلغاء تسجيل المستخدم أو إظهار رسالة خطأ للمستخدم هنا
                ModelState.AddModelError("", "Your account was created, but we couldn't save your profile details to the map. Please contact support.");
                return View(model);
            }

            return RedirectToAction("Login", "Account");
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }
        return View(model);
    }
    // ✅ Login - GET
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    // ✅ Login - POST
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginModel model, string returnUrl = null)
    {
        if (!ModelState.IsValid)
            return View(model);

        var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, false);

        if (result.Succeeded)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user != null)
            {
                var roles = await _userManager.GetRolesAsync(user);
                if (roles.Contains("Donor"))
                {
                    return RedirectToAction("Index", "Donate");
                }
                if (roles.Contains("Beneficiary"))
                {
                    return Redirect("https://arcg.is/1Pj9nH2");
                }
                if (roles.Contains("Charity"))
                {
                    return Redirect("https://experience.arcgis.com/experience/78897f7fc1db424ba77bd1c242532b71");

                    //return RedirectToAction("Index", "Home");
                }
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Index", "Home");
        }

        ModelState.AddModelError(string.Empty, "Invalid login attempt.");
        return View(model);
    }

    // ✅ Logout
    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    // ✅ RegisterDonorAjax - POST
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> RegisterDonorAjax([FromBody] RegisterModel model)
    {
        if (string.IsNullOrWhiteSpace(model.FullName))
            return Json(new { success = false, message = "Full name is required." });
        if (string.IsNullOrWhiteSpace(model.TypeOfDonation))
            return Json(new { success = false, message = "Type of donation is required." });
        if (!model.DonationAmountInEgp.HasValue || model.DonationAmountInEgp <= 0)
            return Json(new { success = false, message = "Donation amount is required." });
        if (string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Password))
            return Json(new { success = false, message = "Email and password are required." });

        // Create Identity user
        var user = new IdentityUser
        {
            UserName = model.Email,
            Email = model.Email
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            var msg = string.Join("; ", result.Errors.Select(e => e.Description));
            return Json(new { success = false, message = msg });
        }

        if (!await _roleManager.RoleExistsAsync("Donor"))
            await _roleManager.CreateAsync(new IdentityRole("Donor"));
        await _userManager.AddToRoleAsync(user, "Donor");

        string preferredAidCategoryStr = string.Empty;
        if (model.PreferredAidCategory != null && model.PreferredAidCategory.Any())
        {
            preferredAidCategoryStr = string.Join(",", model.PreferredAidCategory.Select(cat =>
                cat.Replace(" ", "_").Replace("/", "_/_")));
        }

        var donorFeature = new Dictionary<string, object>
        {
            ["attributes"] = new Dictionary<string, object>
            {
                ["full_name"] = model.FullName ?? string.Empty,
                ["type_of_donation"] = model.TypeOfDonation ?? string.Empty,
                ["donation_amount_in_egp"] = model.DonationAmountInEgp ?? 0,
                ["preferred_aid_category"] = preferredAidCategoryStr,
                ["who_would_you_like_to_donate_to"] = model.WhoWouldYouLikeToDonateTo ?? string.Empty,
                ["enter_your_e_mail"] = model.Email
            },
            ["geometry"] = new Dictionary<string, object>
            {
                ["x"] = model.Longitude ?? 0,
                ["y"] = model.Latitude ?? 0,
                ["spatialReference"] = new Dictionary<string, object> { ["wkid"] = 4326 }
            }
        };

        var featuresList = new List<object> { donorFeature };
        var form = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("features", System.Text.Json.JsonSerializer.Serialize(featuresList)),
            new KeyValuePair<string, string>("f", "json"),
            new KeyValuePair<string, string>("rollbackOnFailure", "false")
        };

        using var httpClient = new System.Net.Http.HttpClient();
        var content = new System.Net.Http.FormUrlEncodedContent(form);
        var response = await httpClient.PostAsync($"{_arcGisSettings.DonorsServiceUrl}/addFeatures", content);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning($"Failed to submit donor data to ArcGIS: {response.StatusCode}");
            return Json(new { success = false, message = "Failed to submit to ArcGIS Feature Layer." });
        }

        return Json(new { success = true });
    }

    // Utility methods
    public async Task<IActionResult> CreateRole(string roleName)
    {
        if (!await _roleManager.RoleExistsAsync(roleName))
        {
            await _roleManager.CreateAsync(new IdentityRole(roleName));
        }
        return RedirectToAction("Index", "Home");
    }

    [AcceptVerbs("Get", "Post")]
    public async Task<IActionResult> IsEmailInUse(string email)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            return Json(true);
        }
        else
        {
            return Json("Account already registered");
        }
    }

    // Expose beneficiaries for DonateController
    public List<BeneficiaryDonationViewModel> GetBeneficiaries()
    {
        return _context.Beneficiaries.Select(b => new BeneficiaryDonationViewModel
        {
            BeneficiaryId = b.BeneficiaryId,
            Name = b.Name,
            NeededAmount = b.NeededAmount,
            DonatedAmount = b.DonatedAmount,
            HelpFields = b.HelpFields.Split(',', System.StringSplitOptions.RemoveEmptyEntries).ToList(),
            UserType = b.UserType
        }).ToList();
    }

}