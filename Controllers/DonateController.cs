using Microsoft.AspNetCore.Mvc;
using Accounts.ViewModels;
using System.Collections.Generic;
using Accounts.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using System;

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
    [Authorize(Roles = "Donor")] 
    public IActionResult Index()
    {
        // Ã·» «·„” ›ÌœÌ‰ „‰ ﬁ«⁄œ… «·»Ì«‰« 
        var beneficiaries = new AccountController(_userManager, null, null, _context).GetBeneficiaries();
        foreach (var b in beneficiaries)
        {
            if (string.IsNullOrEmpty(b.UserType))
            {
                b.UserType = b.Name.Contains("charity", StringComparison.OrdinalIgnoreCase) ? "Charity" : "Beneficiary";
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

    [HttpGet]
    [Authorize(Roles = "Donor")]
    public IActionResult Welcome()
    {
        return View();
    }

    [HttpGet]
    [Authorize(Roles = "Donor")]
    public async Task<IActionResult> ViewDonationHistory(string userType = null, string field = null, DateTime? from = null, DateTime? to = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var donations = await _context.Donations
            .Where(d => d.DonorId == user.Id)
            .OrderByDescending(d => d.Date)
            .ToListAsync();

        var beneficiaries = new AccountController(_userManager, null, null, _context).GetBeneficiaries();
        var beneficiaryRoles = beneficiaries.ToDictionary(b => b.BeneficiaryId, b => b.UserType);
        var beneficiaryFields = beneficiaries.ToDictionary(b => b.BeneficiaryId, b => b.HelpFields ?? new List<string>());

        if (!string.IsNullOrEmpty(userType))
            donations = donations.Where(d => beneficiaryRoles.ContainsKey(d.BeneficiaryId) && beneficiaryRoles[d.BeneficiaryId] == userType).ToList();

        if (!string.IsNullOrEmpty(field))
            donations = donations.Where(d => beneficiaryFields.ContainsKey(d.BeneficiaryId) && beneficiaryFields[d.BeneficiaryId].Contains(field)).ToList();

        if (from.HasValue)
            donations = donations.Where(d => d.Date >= from.Value).ToList();

        if (to.HasValue)
            donations = donations.Where(d => d.Date <= to.Value).ToList();

        ViewBag.Total = donations.Sum(d => d.Amount);
        ViewBag.UserTypes = beneficiaryRoles.Values.Distinct().ToList();
        ViewBag.Fields = beneficiaryFields.Values.SelectMany(f => f).Distinct().ToList();
        ViewBag.BeneficiaryRoles = beneficiaryRoles;
        ViewBag.BeneficiaryFields = beneficiaryFields;

        return View("ViewDonationHistory", donations);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> SubmitDonations([FromBody] List<DonationInputModel> donations)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return Unauthorized();

        var beneficiaries = new AccountController(_userManager, null, null, _context).GetBeneficiaries();
        var results = new List<object>();

        foreach (var d in donations)
        {
            var beneficiary = beneficiaries.FirstOrDefault(b => b.BeneficiaryId == d.BeneficiaryId);
            if (beneficiary == null || d.Amount <= 0 || d.Amount > beneficiary.NeededAmount)
                continue;

            var donation = new Donation
            {
                DonorId = user.Id,
                BeneficiaryId = d.BeneficiaryId,
                BeneficiaryName = beneficiary.Name,
                Amount = d.Amount,
                Date = DateTime.UtcNow,
                BeneficiaryUserType = beneficiary.UserType,
                BeneficiaryHelpFields = string.Join(", ", beneficiary.HelpFields ?? new List<string>())
            };
            _context.Donations.Add(donation);

            //  ÕœÌÀ NeededAmount Ê DonatedAmount ›Ì ﬁ«⁄œ… «·»Ì«‰« 
            var dbBeneficiary = _context.Beneficiaries.FirstOrDefault(b => b.BeneficiaryId == d.BeneficiaryId);
            if (dbBeneficiary != null)
            {
                dbBeneficiary.NeededAmount -= d.Amount;
                if (dbBeneficiary.NeededAmount < 0) dbBeneficiary.NeededAmount = 0;
                dbBeneficiary.DonatedAmount += d.Amount;
            }

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