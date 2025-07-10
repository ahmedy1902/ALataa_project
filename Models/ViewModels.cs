using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace Accounts.ViewModels
{
    public class LoginModel
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Invalid email format.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }

    public class RegisterModel : IValidatableObject
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

        [Required(ErrorMessage = "Please select a role.")]
        public string Role { get; set; } = string.Empty;

        //// Beneficiary-specific fields
        //[Display(Name = "Needed Amount")]
        //public decimal? NeededAmount { get; set; }

        //[Display(Name = "Help Fields")]
        public List<string>? HelpFields { get; set; }

        // Charity-specific fields
        [Display(Name = "Charity Name")]
        public string? CharityName { get; set; }

        [Display(Name = "Charity Sector")]
        public List<string>? CharitySector { get; set; }

        [Display(Name = "Number of Cases Sponsored Monthly")]
        public string? CasesSponsored { get; set; }

        [Display(Name = "Monthly Donation Amount")]
        public string? MonthlyDonation { get; set; }

        [Display(Name = "How Much Do You Need")]
        public double? CharityNeededAmount { get; set; }

        // Donor-specific fields
        [Display(Name = "Full Name")]
        public string? FullName { get; set; }

        [Display(Name = "Type of Donation")]
        public string? TypeOfDonation { get; set; } // "Monthly" or "One Time Only"

        [Display(Name = "Donation Amount in EGP")]
        public double? DonationAmountInEgp { get; set; }

        [Display(Name = "Preferred Aid Category")]
        public List<string>? PreferredAidCategory { get; set; }

        [Display(Name = "Who Would You Like To Donate To")]
        public string? WhoWouldYouLikeToDonateTo { get; set; } // "A charity" or "A direct beneficiary"

        // Hidden GPS coordinates
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        // Custom validation
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            var results = new List<ValidationResult>();


            if (Role == "Charity")
            {
                if (string.IsNullOrWhiteSpace(CharityName))
                {
                    results.Add(new ValidationResult(
                        "Charity name is required.",
                        new[] { nameof(CharityName) }));
                }

                if (CharitySector == null || CharitySector.Count == 0)
                {
                    results.Add(new ValidationResult(
                        "Charity sector is required.",
                        new[] { nameof(CharitySector) }));
                }

                if (string.IsNullOrWhiteSpace(CasesSponsored))
                {
                    results.Add(new ValidationResult(
                        "Number of cases sponsored is required.",
                        new[] { nameof(CasesSponsored) }));
                }

                if (!CharityNeededAmount.HasValue || CharityNeededAmount <= 0)
                {
                    results.Add(new ValidationResult(
                        "Needed amount is required for charities and must be greater than 0.",
                        new[] { nameof(CharityNeededAmount) }));
                }
            }

            return results;
        }

        // Helper properties
        public bool IsBeneficiary => Role == "Beneficiary";
        public bool IsCharity => Role == "Charity";
        public bool IsDonor => Role == "Donor";
    }

    public class BeneficiaryDonationViewModel
    {
        public int BeneficiaryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal NeededAmount { get; set; }
        public decimal DonatedAmount { get; set; }
        public List<string>? HelpFields { get; set; }
        public string UserType { get; set; } = string.Empty;
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