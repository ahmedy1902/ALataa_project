using Microsoft.AspNetCore.Mvc;
using Accounts.ViewModels;
using System.Collections.Generic;

public class DonateController : Controller
{
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
}
