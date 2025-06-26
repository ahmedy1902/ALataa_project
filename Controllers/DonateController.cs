using Microsoft.AspNetCore.Mvc;
using Accounts.ViewModels;
using System.Collections.Generic;
using Accounts.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

public class DonateController : Controller
{
    private readonly AccountContext _context;
    private readonly UserManager<IdentityUser> _userManager;

    public DonateController(AccountContext context, UserManager<IdentityUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    [HttpGet]
    public IActionResult Index()
    {
        // Get beneficiaries from AccountController (in-memory for demo)
        var beneficiaries = AccountController.GetBeneficiaries();
        // Add UserType for each beneficiary (for demo, assume Beneficiary or Charity)
        foreach (var b in beneficiaries)
        {
            if (b.UserType == string.Empty)
            {
                b.UserType = b.Name.Contains("charity", System.StringComparison.OrdinalIgnoreCase) ? "Charity" : "Beneficiary";
            }
        }
        var model = new DonateViewModel
        {
            Beneficiaries = beneficiaries,
            RemainingBalance = 0,
            TotalDonation = 0
        };
        return View("Donate", model);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> SubmitDonations([FromBody] List<DonationInputModel> donations)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return Unauthorized();

        var beneficiaries = AccountController.GetBeneficiaries();
        var results = new List<object>();

        foreach (var d in donations)
        {
            var beneficiary = beneficiaries.FirstOrDefault(b => b.BeneficiaryId == d.BeneficiaryId);
            if (beneficiary == null || d.Amount <= 0 || d.Amount > beneficiary.NeededAmount)
                continue;

            // ”Ã· «· »—⁄
            var donation = new Donation
            {
                DonorId = user.Id,
                BeneficiaryId = d.BeneficiaryId,
                BeneficiaryName = beneficiary.Name,
                Amount = d.Amount,
                Date = System.DateTime.UtcNow
            };
            _context.Donations.Add(donation);

            // Œ’„ «·„»·€
            beneficiary.NeededAmount -= d.Amount;
            if (beneficiary.NeededAmount < 0) beneficiary.NeededAmount = 0;
            beneficiary.DonatedAmount += d.Amount;

            results.Add(new { beneficiary.BeneficiaryId, beneficiary.NeededAmount });
        }
        await _context.SaveChangesAsync();
        return Json(new { success = true, updated = results });
    }

    public class DonationInputModel
    {
        public int BeneficiaryId { get; set; }
        public decimal Amount { get; set; }
    }
}
