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
        // This action populates the initial ViewModel for the Razor page.
        // The actual data for the table/map is loaded via JavaScript for a better user experience.
        var model = new DonateViewModel
        {
            Beneficiaries = new List<BeneficiaryDonationViewModel>() // Initial list is empty
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
        if (user == null) return Unauthorized();

        var donations = await _arcGisService.GetDonationsAsync(user.Email);
        var charities = await _arcGisService.GetCharitiesAsync();
        var needies = await _arcGisService.GetNeediesAsync();

        var accounts = new List<dynamic>();
        accounts.AddRange(charities.Select(c => new { email = c.enter_your_e_mail ?? c.charity_name, name = c.charity_name, userType = "Charity", HelpFields = c.charity_sector }));
        accounts.AddRange(needies.Select(n => new { email = n.email ?? n.full_name, name = n.full_name, userType = "Beneficiary", HelpFields = n.type_of_need }));

        ViewBag.Accounts = accounts;
        return View("ViewDonationHistory", donations);
    }

    /// <summary>
    /// 💡 FIX: The model now includes a 'Type' to differentiate beneficiaries.
    /// This is crucial for preventing donation mismatches.
    /// </summary>
    public class DonationInputModel
    {
        public int BeneficiaryId { get; set; }
        public decimal Amount { get; set; }
        public string Type { get; set; } // "charity" or "needy"
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> SubmitDonations([FromBody] List<DonationInputModel> donations)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var donor = await _arcGisService.GetDonorByEmailAsync(user.Email);
        if (donor == null) return Json(new { success = false, message = "Donor email not found." });

        // Fetch lists once to avoid multiple calls
        var charities = await _arcGisService.GetCharitiesAsync();
        var needies = await _arcGisService.GetNeediesAsync();

        var results = new List<object>();
        double totalDonated = 0;

        foreach (var d in donations)
        {
            if (d.Amount <= 0) continue;

            CharityFeature charity = null;
            NeedyFeature needy = null;

            // 💡 FIX: Search in the correct list based on the Type received from the client.
            // This prevents the bug where a donation for a Needy person could go to a Charity with the same ID.
            if (d.Type == "charity")
            {
                charity = charities.FirstOrDefault(c => c.objectid == d.BeneficiaryId);
            }
            else if (d.Type == "needy")
            {
                needy = needies.FirstOrDefault(n => n.objectid == d.BeneficiaryId);
            }

            if (charity == null && needy == null) continue;

            double currentNeeded = charity?.how_much_do_you_need ?? needy.how_much_do_you_need ?? 0;
            double donationAmount = Math.Min((double)d.Amount, currentNeeded);
            if (donationAmount <= 0) continue;

            var donation = new DonationFeature
            {
                donor_email = user.Email,
                donor_x = donor.x ?? 0,
                donor_y = donor.y ?? 0,
                donation_amount = donationAmount,
                donation_date = DateTime.UtcNow,
                recipient_email = (charity != null) ? charity.enter_your_e_mail : needy.email,
                recipient_name = (charity != null) ? charity.charity_name : needy.full_name,
                donation_field = (charity != null) ? charity.charity_sector : needy.type_of_need,
                recipient_x = charity?.x ?? needy?.x ?? 0,
                recipient_y = charity?.y ?? needy?.y ?? 0
            };

            var added = await _arcGisService.AddDonationAsync(donation);
            if (added)
            {
                totalDonated += donationAmount;
                results.Add(new { id = d.BeneficiaryId, actualAmount = donationAmount });
                double newNeeded = currentNeeded - donationAmount;

                if (charity != null && !string.IsNullOrEmpty(charity.enter_your_e_mail))
                {
                    await _arcGisService.UpdateCharityByEmailAsync(charity.enter_your_e_mail, newNeeded);
                }
                else if (needy != null && !string.IsNullOrEmpty(needy.email))
                {
                    await _arcGisService.UpdateNeedyByEmailAsync(needy.email, newNeeded);
                }
            }
        }
        return Json(new { success = true, updated = results, totalDonated });
    }
}