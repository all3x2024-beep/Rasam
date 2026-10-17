using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace Rasam.Models.Accounting
{
    public class Role : IdentityRole
    {

        [NotMapped]
        public string? TempUserId { get; set; }


    }
 
}
