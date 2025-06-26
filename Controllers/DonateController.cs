using Microsoft.AspNetCore.Mvc;
using Accounts.ViewModels;
using System.Collections.Generic;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Threading.Tasks;
using Accounts.Services;
using System.Linq;

public class DonateController : Controller
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ArcGisService _arcGisService;

    public DonateController(UserManager<IdentityUser> userManager, ArcGisService arcGisService)
    {
        _userManager = userManager;
        _arcGisService = arcGisService;
    }

    [HttpGet]
    [Authorize(Roles = "Donor")]
    public async Task<IActionResult> Index()
    {
        var charities = await _arcGisService.GetCharitiesAsync();
        var needies = await _arcGisService.GetNeediesAsync();
        var beneficiaries = new List<BeneficiaryDonationViewModel>();

        beneficiaries.AddRange(charities.Select(c => new BeneficiaryDonationViewModel
        {
            BeneficiaryId = c.objectid ?? 0,
            Name = c.charity_name,
            NeededAmount = (decimal)(c.how_much_do_you_need ?? 0), // cast to decimal
            DonatedAmount = 0,
            HelpFields = new List<string> { c.charity_sector },
            UserType = "Charity"
        }));

        beneficiaries.AddRange(needies.Select(n => new BeneficiaryDonationViewModel
        {
            BeneficiaryId = n.objectid ?? 0,
            Name = n.full_name,
            NeededAmount = (decimal)(n.how_much_do_you_need ?? 0), // cast to decimal
            DonatedAmount = 0,
            HelpFields = new List<string> { n.type_of_need },
            UserType = "Beneficiary"
        }));

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
    public async Task<IActionResult> ViewDonationHistory()
    {
        // Ì„ﬂ‰ Ã·» «· »—⁄«  „‰ ArcGIS ≈–« √—œ  ⁄—÷Â«
        return View("ViewDonationHistory", new List<object>()); // ⁄œ· Õ”» «·Õ«Ã…
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> SubmitDonations([FromBody] List<DonationInputModel> donations)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return Unauthorized();

        var charities = await _arcGisService.GetCharitiesAsync();
        var needies = await _arcGisService.GetNeediesAsync();
        var all = new List<dynamic>();
        all.AddRange(charities);
        all.AddRange(needies);
        var results = new List<object>();

        foreach (var d in donations)
        {
            var beneficiary = all.FirstOrDefault(b => (b.objectid ?? 0) == d.BeneficiaryId);
            if (beneficiary == null || d.Amount < 1 || d.Amount > 1000000)
                continue;

            string name = beneficiary.charity_name ?? beneficiary.full_name;
            string field = beneficiary.charity_sector ?? beneficiary.type_of_need;
            string userType = beneficiary.charity_name != null ? "Charity" : "Beneficiary";

            var donation = new DonationFeature
            {
                donor_name = user.Email,
                recipient_name = name,
                donation_field = field,
                donation_date = DateTime.UtcNow
            };
            await _arcGisService.AddDonationAsync(donation, 0, 0);
            results.Add(new { id = d.BeneficiaryId });
        }
        return Json(new { success = true, updated = results });
    }

    public class DonationInputModel
    {
        public int BeneficiaryId { get; set; }
        public decimal Amount { get; set; }
    }
}