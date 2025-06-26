using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Accounts.ViewModels;
using Microsoft.AspNetCore.Authorization;
using System.Linq;
using System.Collections.Generic;

public class AccountController : Controller
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    // In-memory beneficiaries list for demo (replace with DB in production)
    private static List<BeneficiaryDonationViewModel> Beneficiaries = new List<BeneficiaryDonationViewModel>();

    public AccountController(UserManager<IdentityUser> userManager,
                             SignInManager<IdentityUser> signInManager,
                             RoleManager<IdentityRole> roleManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
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
        // Custom validation for beneficiary and charity fields
        if (model.Role == "Beneficiary" || model.Role == "Charity")
        {
            if (!model.NeededAmount.HasValue || model.NeededAmount <= 0)
                ModelState.AddModelError("NeededAmount", "Needed amount is required for this role.");
            if (model.HelpFields == null || !model.HelpFields.Any())
                ModelState.AddModelError("HelpFields", "Please select at least one help field.");
        }

        if (!ModelState.IsValid)
        {
            ModelState.AddModelError("RegisterError", "ModelState is invalid. Please check your input.");
            return View(model);
        }

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

            // If beneficiary or charity, add to in-memory list (replace with DB in production)
            if ((model.Role == "Beneficiary" || model.Role == "Charity") && model.NeededAmount.HasValue && model.HelpFields != null)
            {
                Beneficiaries.Add(new BeneficiaryDonationViewModel
                {
                    BeneficiaryId = Beneficiaries.Count + 1,
                    Name = model.Email,
                    NeededAmount = model.NeededAmount.Value,
                    DonatedAmount = 0,
                    HelpFields = model.HelpFields,
                    UserType = model.Role
                });
            }

            // لا تقم بتسجيل الدخول تلقائياً
            // await _signInManager.SignInAsync(user, isPersistent: false);
            return RedirectToAction("Login", "Account");
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError("RegisterError", error.Description);
        }

        // Log for debug
        ModelState.AddModelError("RegisterError", "User creation failed. Check errors above.");
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
                    // يمكن توجيه المستفيد لصفحة معينة أو رسالة
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
            return Json("Account already registed");
        }
    }

    // Expose beneficiaries for DonateController
    public static List<BeneficiaryDonationViewModel> GetBeneficiaries() => Beneficiaries;
}
