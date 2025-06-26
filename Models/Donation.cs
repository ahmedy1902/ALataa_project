using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace Accounts.Models
{
    public class Donation
    {
        public int Id { get; set; }
        [Required]
        public string DonorId { get; set; } = string.Empty; // FK to IdentityUser
        public IdentityUser? Donor { get; set; }
        [Required]
        public int BeneficiaryId { get; set; } // FK to BeneficiaryDonationViewModel
        public string BeneficiaryName { get; set; } = string.Empty;
        [Required]
        public decimal Amount { get; set; }
        public DateTime Date { get; set; } = DateTime.UtcNow;
    }
}
