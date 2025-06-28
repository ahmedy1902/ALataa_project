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

public class AccountController : Controller
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly AccountContext _context;
    private readonly ArcGisService _arcGisService;
    private readonly ILogger<AccountController> _logger;

    public AccountController(UserManager<IdentityUser> userManager,
                             SignInManager<IdentityUser> signInManager,
                             RoleManager<IdentityRole> roleManager,
                             AccountContext context,
                             ArcGisService arcGisService,
                             ILogger<AccountController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _context = context;
        _arcGisService = arcGisService;
        _logger = logger;
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
        // Role-based validation

         if (model.Role == "Charity")
        {
            if (string.IsNullOrWhiteSpace(model.CharityName))
                ModelState.AddModelError("CharityName", "Charity name is required.");
            if (string.IsNullOrWhiteSpace(model.CharitySector))
                ModelState.AddModelError("CharitySector", "Charity sector is required.");
            if (string.IsNullOrWhiteSpace(model.CasesSponsored))
                ModelState.AddModelError("CasesSponsored", "Number of cases sponsored is required.");
            if (!model.CharityNeededAmount.HasValue || model.CharityNeededAmount <= 0)
                ModelState.AddModelError("CharityNeededAmount", "Needed amount is required for charities.");
        }
        else if (model.Role == "Donor")
        {
            // تحقق من الحقول المطلوبة للمتبرع
            if (string.IsNullOrWhiteSpace(model.FullName))
                ModelState.AddModelError("FullName", "Full name is required for donors.");
            if (string.IsNullOrWhiteSpace(model.TypeOfDonation))
                ModelState.AddModelError("TypeOfDonation", "Type of donation is required for donors.");
            if (!model.DonationAmountInEgp.HasValue || model.DonationAmountInEgp <= 0)
                ModelState.AddModelError("DonationAmountInEgp", "Donation amount is required for donors.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // Create Identity user
        var user = new IdentityUser
        {
            UserName = model.Email,
            Email = model.Email
        };

        var result = await _userManager.CreateAsync(user, model.Password);

        if (result.Succeeded)
        {
            // Add user to selected role
            if (!string.IsNullOrEmpty(model.Role))
            {
                if (!await _roleManager.RoleExistsAsync(model.Role))
                    await _roleManager.CreateAsync(new IdentityRole(model.Role));

                await _userManager.AddToRoleAsync(user, model.Role);
            }

            // Handle role-specific data

             if (model.Role == "Charity")
            {
                // Send charity data directly to ArcGIS Feature Layer using HttpClient
                var feature = new Dictionary<string, object>
                {
                    ["attributes"] = new Dictionary<string, object>
                    {
                        ["charity_name"] = model.CharityName,
                        ["charity_sector"] = model.CharitySector,
                        ["field_9"] = model.CasesSponsored,
                        ["field_10"] = model.MonthlyDonation,
                        ["how_much_do_you_need"] = model.CharityNeededAmount,
                        ["enter_your_e_mail"] = model.Email
                    },
                    ["geometry"] = new Dictionary<string, object>
                    {
                        ["x"] = model.Longitude ?? 0,
                        ["y"] = model.Latitude ?? 0,
                        ["spatialReference"] = new Dictionary<string, object> { ["wkid"] = 4326 }
                    }
                };
                var featuresList = new List<object> { feature };
                var form = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("features", System.Text.Json.JsonSerializer.Serialize(featuresList)),
                    new KeyValuePair<string, string>("f", "json"),
                    new KeyValuePair<string, string>("rollbackOnFailure", "false")
                };
                using var httpClient = new System.Net.Http.HttpClient();
                var content = new System.Net.Http.FormUrlEncodedContent(form);
                var response = await httpClient.PostAsync(
                    "https://services.arcgis.com/LxyOyIfeECQuFOsk/arcgis/rest/services/survey123_2c36d5ade9064fe685d54893df3b37ea/FeatureServer/0/addFeatures",
                    content);
                if (!response.IsSuccessStatusCode)
                {
                    ModelState.AddModelError("RegisterError", "Failed to submit to ArcGIS Feature Layer.");
                    return View(model);
                }
            }
            else if (model.Role == "Donor")
            {
                // Send donor data to ArcGIS (with GPS)
                var donorPayload = new {
                    features = new[] {
                        new {
                            attributes = new Dictionary<string, object>
                            {
                                ["full_name"] = model.FullName ?? string.Empty,
                                ["type_of_donation"] = model.TypeOfDonation ?? string.Empty,
                                ["donation_amount_in_egp"] = model.DonationAmountInEgp ?? 0,
                                ["preferred_aid_category"] = model.PreferredAidCategory ?? string.Empty,
                                ["who_would_you_like_to_donate_to"] = model.WhoWouldYouLikeToDonateTo ?? string.Empty,
                                ["enter_your_e_mail"] = model.Email
                            },
                            geometry = new {
                                x = model.Longitude ?? 0,
                                y = model.Latitude ?? 0,
                                spatialReference = new { wkid = 4326 }
                            }
                        }
                    },
                    f = "json"
                };
                var donorJson = System.Text.Json.JsonSerializer.Serialize(donorPayload);
                using var client = new System.Net.Http.HttpClient();
                var resp = await client.PostAsync(
                    "https://services.arcgis.com/LxyOyIfeECQuFOsk/arcgis/rest/services/survey123_fb464f56faae4b6c803825277c69be1c/FeatureServer/0/addFeatures",
                    new System.Net.Http.StringContent(donorJson, System.Text.Encoding.UTF8, "application/json"));
                // Optionally check resp.IsSuccessStatusCode
            }

            return RedirectToAction("Login", "Account");
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError("RegisterError", error.Description);
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
                    return RedirectToAction("Index", "Home");
                }
                if (roles.Contains("Charity"))
                {
                    return RedirectToAction("Index", "Home");
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

        // Send donor data to ArcGIS (with GPS)
        var donorPayload = new {
            features = new[] {
                new {
                    attributes = new Dictionary<string, object>
                    {
                        ["full_name"] = model.FullName ?? string.Empty,
                        ["type_of_donation"] = model.TypeOfDonation ?? string.Empty,
                        ["donation_amount_in_egp"] = model.DonationAmountInEgp ?? 0,
                        ["preferred_aid_category"] = model.PreferredAidCategory ?? string.Empty,
                        ["who_would_you_like_to_donate_to"] = model.WhoWouldYouLikeToDonateTo ?? string.Empty,
                        ["enter_your_e_mail"] = model.Email
                    },
                    geometry = new {
                        x = model.Longitude ?? 0,
                        y = model.Latitude ?? 0,
                        spatialReference = new { wkid = 4326 }
                    }
                }
            },
            f = "json"
        };
        var donorJson = System.Text.Json.JsonSerializer.Serialize(donorPayload);
        using var client = new System.Net.Http.HttpClient();
        var resp = await client.PostAsync(
            "https://services.arcgis.com/LxyOyIfeECQuFOsk/arcgis/rest/services/survey123_fb464f56faae4b6c803825277c69be1c/FeatureServer/0/addFeatures",
            new System.Net.Http.StringContent(donorJson, System.Text.Encoding.UTF8, "application/json"));
        if (!resp.IsSuccessStatusCode)
            return Json(new { success = false, message = "Failed to submit to ArcGIS Feature Layer." });

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