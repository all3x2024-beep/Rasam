using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace Rasam.Models.Accounting
{
    public class Role : IdentityRole
    {

        [NotMapped]
        public string? TempUserId { get; set; }


    }
    public static class AppRoles
    {
        public const string Admin = "Admin";     // همه کارها
        public const string Operator = "Operator";  // ثبت/ویرایش رکورد
        public const string Viewer = "Viewer";    // فقط مشاهده
    }
}
