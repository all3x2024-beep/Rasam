
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace Rasam.Models.SocialAccount
{
    public class Profile
    {

        [Key]
        public int Id { get; set; }

        [PersonalData]
        [DisplayName("نام کاربری")]
        [MaxLength(100)]
        public string? UsernameId { get; set; } = null!;

        [PersonalData]
        [DisplayName("شناسه شخصی")]
        [MaxLength(100)]
        public string? PID { get; set; }

        [PersonalData]
        [DisplayName("نام")]
        [MaxLength(100)]
        public string? FirstName { get; set; }

        [PersonalData]
        [DisplayName("نام خانوادگی")]
        [MaxLength(100)]
        public string? LastName { get; set; }

        [PersonalData]
        [DisplayName("کد ملی")]
        [MaxLength(20)]
        public string? NationalCode { get; set; }

        [PersonalData]
        [DisplayName("موبایل")]
        [MaxLength(30)]
        public string? Phone { get; set; }

        [PersonalData]
        [DisplayName("کشور")]
        [MaxLength(100)]
        public string? Country { get; set; }
        [DisplayName("تصویر")]
        [MaxLength(500)]
        public string? Image { get; set; }

        [NotMapped]
        public IFormFile? MyFile { get; set; }

        [NotMapped]
        public List<IFormFile>? AttachmentsFiles { get; set; }

        public ICollection<ProfileAttachment> Attachments { get; set; }
            = new List<ProfileAttachment>();

        [DisplayName("موضوع")]
        public int? TopicId { get; set; }
        public Models.MetaData.Topic? Topic { get; set; }

        public ICollection<Models.MetaData.Tag> Tag { get; set; }
            = new List<Models.MetaData.Tag>();

        public ICollection<Models.MetaData.Relation> Relation { get; set; }
            = new List<Models.MetaData.Relation>();

        public ICollection<Models.MetaData.SocialMedia> SocialMedia { get; set; }
            = new List<Models.MetaData.SocialMedia>();

        [DisplayName("نظریه")]
        [Column(TypeName = "nvarchar(max)")]
        public string? Theory { get; set; }

        public string? CreateDate { get; set; }
        public string? CreateBY { get; set; }

        public string? ModifyDate { get; set; }
        public string? ModifyBY { get; set; }

    
        public ICollection<ProfileRelation> OutgoingRelations { get; set; }
     = new List<ProfileRelation>();

        public ICollection<ProfileRelation> IncomingRelations { get; set; }
            = new List<ProfileRelation>();

    }
}
