using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace Rasam.Models.MetaData
{
    public class Relation
    {
       [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]

        public int Id { get; set; }

        [DisplayName("نوع ارتباط")]
        [Required(ErrorMessage = "نوع ارتباط الزامی است.")]
        [MaxLength(100)]
        public string Type { get; set; } = string.Empty;



    }
}