using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace Accounts.ViewModels
{
    public class LoginModel
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool RememberMe { get; set; }
    }

    public class RegisterModel
    {
        [Remote(action: "IsEmailInUse", controller: "Account")]
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Invalid email format.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        [StringLength(15, MinimumLength = 6, ErrorMessage = "Password must be between 6 and 15 characters.")]
        public string Password { get; set; } = string.Empty;

        [Compare("Password", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;

        // Beneficiary-specific fields
        [Display(Name = "Needed Amount")]
        public decimal? NeededAmount { get; set; }

        [Display(Name = "Help Field")]
        public List<string>? HelpFields { get; set; }

        // Helper for server-side validation
        public bool IsBeneficiary => Role == "Beneficiary";
    }

    public class BeneficiaryDonationViewModel
    {
        public int BeneficiaryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal NeededAmount { get; set; }
        public decimal DonatedAmount { get; set; }
        public List<string>? HelpFields { get; set; }
    }

    public class DonateViewModel
    {
        [Required]
        [Range(1, 1000000, ErrorMessage = "Please enter a valid donation amount.")]
        public decimal TotalDonation { get; set; }
        public decimal RemainingBalance { get; set; }
        public List<BeneficiaryDonationViewModel> Beneficiaries { get; set; } = new();
        public List<BeneficiaryDonationViewModel> Donations { get; set; } = new();
    }
}
