using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Rasam.Models.MetaData
{
    public class SocialMedia
    {
        [Key]
        public int Id { get; set; }

        [DisplayName("نام شبکه اجتماعی")]
        [Required(ErrorMessage = "نام شبکه اجتماعی الزامی است.")]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [DisplayName("حساب کاربری")]
        [Required(ErrorMessage = "حساب کاربری الزامی است.")]
        [MaxLength(100)]
        public string Username { get; set; } = string.Empty;


    }
}