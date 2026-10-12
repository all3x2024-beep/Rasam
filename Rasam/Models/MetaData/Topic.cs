using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace Rasam.Models.MetaData
{
    public class Topic
    {
        [Key]
        public int Id { get; set; }

        [DisplayName("موضوع")]
        [Required(ErrorMessage = "موضوع الزامی است.")]
        [MaxLength(100)]
        public string Title { get; set; } = string.Empty;




    }
}
