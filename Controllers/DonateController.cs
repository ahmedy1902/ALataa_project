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
        var model = new DonateViewModel
        {
            Beneficiaries = beneficiaries,
            RemainingBalance = 0,
            TotalDonation = 0
        };
        return View("Donate", model);
    }
}
