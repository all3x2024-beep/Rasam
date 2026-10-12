using Microsoft.AspNetCore.Identity;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Rasam.Models.MetaData
{
    public class Relation
    {
       [Key]
       public int Id { get; set; }

        [DisplayName("نوع ارتباط")]
        [Required(ErrorMessage = "نوع ارتباط الزامی است.")]
        [MaxLength(100)]
        public string Type { get; set; } = string.Empty;



    }
}