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
            NeededAmount = (decimal)(c.how_much_do_you_need ?? 0),
            DonatedAmount = 0,
            HelpFields = new List<string> { c.charity_sector },
            UserType = "Charity"
        }));

        beneficiaries.AddRange(needies.Select(n => new BeneficiaryDonationViewModel
        {
            BeneficiaryId = n.objectid ?? 0,
            Name = n.full_name,
            NeededAmount = (decimal)(n.how_much_do_you_need ?? 0),
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

        // Ã·» ﬁ«∆„… «·„” ›ÌœÌ‰ „⁄  ›«’Ì·Â„
        var charities = await _arcGisService.GetCharitiesAsync();
        var needies = await _arcGisService.GetNeediesAsync();
        var accounts = new List<dynamic>();

        accounts.AddRange(charities.Select(c => new {
            email = c.charity_name,
            userType = "Charity",
            HelpFields = c.charity_sector
        }));

        accounts.AddRange(needies.Select(n => new {
            email = n.full_name,
            userType = "Beneficiary",
            HelpFields = n.type_of_need
        }));

        ViewBag.Accounts = accounts;

        // ≈÷«›… ﬁÊ«∆„ „‰›’·… ··›·« —
        ViewBag.UserTypes = accounts.Select(a => a.userType).Distinct().ToList();
        ViewBag.Fields = accounts.Select(a => a.HelpFields).Distinct().ToList();

        return View("ViewDonationHistory", donations);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> SubmitDonations([FromBody] List<DonationInputModel> donations)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return Unauthorized();

        // Ã·» »Ì«‰«  «·„ »—⁄ „‰ ÿ»ﬁ… «·„ »—⁄Ì‰
        var donor = await _arcGisService.GetDonorByEmailAsync(user.Email);
        if (donor == null)
            return Json(new { success = false, message = "Donor email not found in donors layer." });

        var charities = await _arcGisService.GetCharitiesAsync();
        var needies = await _arcGisService.GetNeediesAsync();
        var results = new List<object>();
        double totalDonated = 0;

        foreach (var d in donations)
        {
            if (d.Amount < 1 || d.Amount > 1000000)
                continue;

            // «·»ÕÀ ⁄‰ «·„” ›Ìœ ›Ì ﬂ·« «·ÿ»ﬁ Ì‰
            var charity = charities.FirstOrDefault(c => (c.objectid ?? 0) == d.BeneficiaryId);
            var needy = needies.FirstOrDefault(n => (n.objectid ?? 0) == d.BeneficiaryId);

            string recipientName, recipientField, userType;
            double recipientX = 0, recipientY = 0;
            double currentNeeded = 0;

            if (charity != null)
            {
                recipientName = charity.charity_name;
                recipientField = charity.charity_sector;
                userType = "Charity";
                recipientX = charity.x ?? 0;
                recipientY = charity.y ?? 0;
                currentNeeded = charity.how_much_do_you_need ?? 0;
            }
            else if (needy != null)
            {
                recipientName = needy.full_name;
                recipientField = needy.type_of_need;
                userType = "Beneficiary";
                recipientX = needy.x ?? 0;
                recipientY = needy.y ?? 0;
                currentNeeded = needy.how_much_do_you_need ?? 0;
            }
            else
            {
                continue; //  ŒÿÌ ≈–« ·„ Ì „ «·⁄ÀÊ— ⁄·Ï «·„” ›Ìœ
            }

            // «· √ﬂœ „‰ √‰ «·„»·€ «·„ÿ·Ê» ·· »—⁄ ·« Ì Ã«Ê“ «·„»·€ «·„ÿ·Ê»
            double donationAmount = (double)d.Amount;
            if (donationAmount > currentNeeded)
            {
                donationAmount = currentNeeded; //  ÕœÌœ «·„»·€ »«·Õœ «·√ﬁ’Ï «·„ÿ·Ê»
            }

            if (donationAmount <= 0)
                continue; //  ŒÿÌ ≈–« ·„ Ì⁄œ Â‰«ﬂ Õ«Ã… ·· »—⁄

            // ≈Õœ«ÀÌ«  «·„ »—⁄ „‰ ÿ»ﬁ… «·„ »—⁄Ì‰
            double donorX = donor.x ?? 0;
            double donorY = donor.y ?? 0;

            // ≈‰‘«¡ ”Ã· «· »—⁄
            var donation = new DonationFeature
            {
                donor_email = user.Email,
                recipient_email = recipientName,
                donation_field = recipientField,
                donation_amount = donationAmount,
                donation_date = DateTime.UtcNow,
                donor_x = donorX,
                donor_y = donorY,
                recipient_x = recipientX,
                recipient_y = recipientY
            };

            var added = await _arcGisService.AddDonationAsync(donation);
            if (added)
            {
                totalDonated += donationAmount;
                results.Add(new
                {
                    id = d.BeneficiaryId,
                    actualAmount = donationAmount,
                    requestedAmount = (double)d.Amount
                });

                //  ÕœÌÀ «·„»·€ «·„ÿ·Ê» ··„” ›Ìœ ›Ì «·ÿ»ﬁ… «·’ÕÌÕ…
                double newNeeded = Math.Max(0, currentNeeded - donationAmount);

                if (charity != null)
                {
                    //  ÕœÌÀ «·Ã„⁄Ì… «·ŒÌ—Ì…
                    await _arcGisService.UpdateCharityNeededAmountAsync(charity.objectid ?? 0, newNeeded);
                }
                else if (needy != null)
                {
                    //  ÕœÌÀ «·„Õ «Ã
                    await _arcGisService.UpdateNeedyNeededAmountAsync(needy.objectid ?? 0, newNeeded);
                }
            }
        }

        return Json(new
        {
            success = true,
            updated = results,
            totalDonated = totalDonated,
            message = $"A donation of {totalDonated} has been made successfully!"
        });
    }

    public class DonationInputModel
    {
        public int BeneficiaryId { get; set; }
        public decimal Amount { get; set; }
    }
}