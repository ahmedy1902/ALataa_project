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
        if (model.Role == "Beneficiary")
        {
            if (!model.NeededAmount.HasValue || model.NeededAmount <= 0)
                ModelState.AddModelError("NeededAmount", "Needed amount is required for beneficiaries.");
            if (model.HelpFields == null || !model.HelpFields.Any())
                ModelState.AddModelError("HelpFields", "Please select at least one help field.");
        }
        else if (model.Role == "Charity")
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
            if (model.Role == "Beneficiary" && model.NeededAmount.HasValue && model.HelpFields != null)
            {
                var beneficiary = new Beneficiary
                {
                    Name = model.Email,
                    NeededAmount = model.NeededAmount.Value,
                    DonatedAmount = 0,
                    HelpFields = string.Join(",", model.HelpFields),
                    UserType = model.Role
                };
                _context.Beneficiaries.Add(beneficiary);
                await _context.SaveChangesAsync();
            }
            else if (model.Role == "Charity")
            {
                // Send charity data to ArcGIS
                await _arcGisService.SendCharityDataAsync(model);
            }
            else if (model.Role == "Donor")
            {
                // Send donor data to ArcGIS
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