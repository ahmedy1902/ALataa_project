using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Models
{
    public class AccountContext : IdentityDbContext<IdentityUser>
    {
        public AccountContext(DbContextOptions<AccountContext> options) : base(options) { }
    }
}
