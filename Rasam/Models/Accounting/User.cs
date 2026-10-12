using Microsoft.AspNetCore.Identity;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace Rasam.Models.Accounting
{
    public class User:IdentityUser 
    {
        [PersonalData]
        [DisplayName("نام")]
        public string? FirstName { get; set; }

        [PersonalData]
        [DisplayName("نام خانوادگی")]
        public string? LastName { get; set; }

        [DisplayName("تصویر")]
        public string? Image { get; set; }

        [PersonalData]
        [DisplayName("تلفن")]
        public string? Phone { get; set; }

        [PersonalData]
        [DisplayName("استان")]
        public string? City { get; set; }



        [NotMapped]
        [DisplayName("نام کاربری")]
        public string? UsernameTemp { get; set; }

        [NotMapped]
        [DisplayName("رمزعبور")]
        public string? PassWordTemp { get; set; }




        [DisplayName("نقش")]
        public string? RoleTemp { get; set; }


        [NotMapped]
        public IFormFile? MyFile { get; set; }

        public string? DateRegister { get; set; }
        public string? CreateBY { get; set; }

    }
}
