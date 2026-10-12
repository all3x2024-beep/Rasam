
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Rasam.Models.MetaData;

namespace Rasam.Models.SocialAccount
{

    public class ProfileRelation
    {
        [Key]
        public int Id { get; set; }

        public int FromProfileId { get; set; }

        public int ToProfileId { get; set; }

        public int RelationId { get; set; }

        public Profile FromProfile { get; set; } = null!;

        public Profile ToProfile { get; set; } = null!;

        public Relation Relation { get; set; } = null!;


    }
}
