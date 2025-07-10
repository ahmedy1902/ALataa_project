using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    public class Beneficiary
    {
        [Key]
        public int BeneficiaryId { get; set; }
        [Required]
        public string Name { get; set; } = string.Empty;
        [Required]
        public decimal NeededAmount { get; set; }
        public decimal DonatedAmount { get; set; } = 0;
        public string UserType { get; set; } = string.Empty; // Beneficiary or Charity
        public string HelpFields { get; set; } = string.Empty; // CSV
    }
}
