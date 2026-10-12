using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace Rasam.Models.MetaData
{
    public class Tag
    {
        [Key]
        public int Id { get; set; }

        [DisplayName("تگ")]
        [Required(ErrorMessage = "عنوان الزامی است.")]
        [MaxLength(100)]
        public string Title { get; set; } = string.Empty;
    }
}
