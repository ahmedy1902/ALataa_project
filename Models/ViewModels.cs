using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

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
    }
}
