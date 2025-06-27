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
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return Unauthorized();

        var donations = await _arcGisService.GetDonationsAsync(user.Email);
        // Ã·» ﬁ«∆„… «·„” ›ÌœÌ‰ (charities + needies) · „—Ì—Â« ··›ÌÊ
        var charities = await _arcGisService.GetCharitiesAsync();
        var needies = await _arcGisService.GetNeediesAsync();
        var accounts = new List<dynamic>();
        accounts.AddRange(charities.Select(c => new { email = c.charity_name, userType = "Charity", HelpFields = c.charity_sector }));
        accounts.AddRange(needies.Select(n => new { email = n.full_name, userType = "Beneficiary", HelpFields = n.type_of_need }));
        ViewBag.Accounts = accounts;
        return View("ViewDonationHistory", donations);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> SubmitDonations([FromBody] List<DonationInputModel> donations)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return Unauthorized();

        // Ã·» »Ì«‰«  «·„ »—⁄ „‰ ·«Ì— «·„ »—⁄Ì‰
        var donor = await _arcGisService.GetDonorByEmailAsync(user.Email);
        if (donor == null)
            return Json(new { success = false, message = "Donor email not found in donors layer." });

        var charities = await _arcGisService.GetCharitiesAsync();
        var needies = await _arcGisService.GetNeediesAsync();
        var all = new List<dynamic>();
        all.AddRange(charities);
        all.AddRange(needies);
        var results = new List<object>();

        double totalDonated = 0;
        foreach (var d in donations)
        {
            var beneficiary = all.FirstOrDefault(b => (b.objectid ?? 0) == d.BeneficiaryId);
            if (beneficiary == null || d.Amount < 1 || d.Amount > 1000000)
                continue;

            string name, field, userType;
            if (beneficiary is CharityFeature charity)
            {
                name = charity.charity_name;
                field = charity.charity_sector;
                userType = "Charity";
            }
            else if (beneficiary is NeedyFeature needy)
            {
                name = needy.full_name;
                field = needy.type_of_need;
                userType = "Beneficiary";
            }
            else
            {
                continue;
            }

            // ≈Õœ«ÀÌ«  «·ÃÂ… «·„” ›Ìœ…
            double recipient_x = beneficiary.x ?? 0;
            double recipient_y = beneficiary.y ?? 0;

            // ≈Õœ«ÀÌ«  «·„ »—⁄ „‰ ·«Ì— «·„ »—⁄Ì‰
            double donor_x = donor.x ?? 0;
            double donor_y = donor.y ?? 0;

            var donation = new DonationFeature
            {
                donor_email = user.Email,
                recipient_email = name,
                donation_field = field,
                donation_amount = (double)d.Amount,
                donation_date = DateTime.UtcNow,
                donor_x = donor_x,
                donor_y = donor_y,
                recipient_x = recipient_x,
                recipient_y = recipient_y
            };
            var added = await _arcGisService.AddDonationAsync(donation);
            if (added)
            {
                totalDonated += (double)d.Amount;
                results.Add(new { id = d.BeneficiaryId });
                // Œ’„ «· »—⁄ „‰ needed amount ··„” ›Ìœ
                if (beneficiary.how_much_do_you_need != null)
                {
                    double newNeeded = Math.Max(0, (beneficiary.how_much_do_you_need ?? 0) - (double)d.Amount);
                    await _arcGisService.UpdateDonorNeededAmountAsync(name, newNeeded);
                }
                // Œ’„ «· »—⁄ „‰ needed amount ··„ »—⁄ ‰›”Â
                if (donor.how_much_do_you_need != null)
                {
                    double newDonorNeeded = Math.Max(0, (donor.how_much_do_you_need ?? 0) - (double)d.Amount);
                    await _arcGisService.UpdateDonorNeededAmountAsync(user.Email, newDonorNeeded);
                }
            }
        }
        // Œ’„ «· »—⁄ „‰ needed amount
        if (totalDonated > 0 && donor.how_much_do_you_need.HasValue)
        {
            double newNeeded = Math.Max(0, donor.how_much_do_you_need.Value - totalDonated);
            await _arcGisService.UpdateDonorNeededAmountAsync(user.Email, newNeeded);
        }
        return Json(new { success = true, updated = results });
    }

    public class DonationInputModel
    {
        public int BeneficiaryId { get; set; }
        public decimal Amount { get; set; }
    }
}