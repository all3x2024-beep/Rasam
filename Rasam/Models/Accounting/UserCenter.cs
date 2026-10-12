using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel;

namespace Rasam.Models.Accounting
{
    public class UserCenter
    {
        [ScaffoldColumn(false)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public string? Id { get; set; }

        [DisplayName("نام")]
        public string? FirstName { get; set; }

        [DisplayName("نام خانوادگی")]
        public string? LastName { get; set; }

        [DisplayName("شناسا")]
        public string? Code { get; set; }

        public bool IsSuperAdmin { get; set; } = false;
        public bool IsAdmin { get; set; } = false;
        public bool IsUser { get; set; } = false;
    }
}
