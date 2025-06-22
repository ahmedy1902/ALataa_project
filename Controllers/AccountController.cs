using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Accounts.ViewModels;
using Microsoft.AspNetCore.Authorization;

public class AccountController : Controller
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly RoleManager<IdentityRole> _roleManager;

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
        if (!ModelState.IsValid)
            return View(model);

        var user = new IdentityUser { UserName = model.Email, Email = model.Email };
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
            await _signInManager.SignInAsync(user, isPersistent: false);
            return RedirectToAction("Index", "Home");
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
}
