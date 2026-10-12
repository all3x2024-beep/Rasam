using System.ComponentModel.DataAnnotations;
using Rasam.Models.SocialAccount;

namespace Rasam.Models.SocialAccount
{
    public class ProfileAttachment
    {
        [Key]
        public int Id { get; set; }

        public int ProfileId { get; set; }

        public Profile Profile { get; set; } = null!;

        [MaxLength(255)]
        public string FileName { get; set; } = null!;

        [MaxLength(1000)]
        public string FilePath { get; set; } = null!;

        [MaxLength(1000)]
        public string FileUrl { get; set; } = null!;

        public string? CreateDate { get; set; }

        public string? CreateBY { get; set; }
    }
}