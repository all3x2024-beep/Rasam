using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Rasam.Models.Accounting;
using Rasam.Models.MetaData;
using Rasam.Models.SocialAccount;

namespace Rasam.Data
{
    public class ApplicationDbContext : IdentityDbContext<User, Role, string>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
        {
        }


        public DbSet<Profile> Profile { get; set; } = default!;
        public DbSet<Relation> Relation { get; set; } = default!;
        public DbSet<Tag> Tag { get; set; } = default!;
        public DbSet<Topic> Topic { get; set; } = default!;
        public DbSet<ProfileRelation> ProfileRelation { get; set; } = default!;
        public DbSet<ProfileAttachment> ProfileAttachment { get; set; } = default!;
        public DbSet<SocialMedia> SocialMedia { get; set; } = default!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ProfileRelation>()
                .HasOne(x => x.FromProfile)
                .WithMany(x => x.OutgoingRelations)
                .HasForeignKey(x => x.FromProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ProfileRelation>()
                .HasOne(x => x.ToProfile)
                .WithMany(x => x.IncomingRelations)
                .HasForeignKey(x => x.ToProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ProfileRelation>()
                .HasOne(x => x.Relation)
                .WithMany()
                .HasForeignKey(x => x.RelationId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ProfileRelation>()
                .HasIndex(x => new
                {
                    x.FromProfileId,
                    x.ToProfileId,
                    x.RelationId
                })
                .IsUnique();

            modelBuilder.Entity<Profile>()
                .HasIndex(x => x.UsernameId)
                .IsUnique();

            modelBuilder.Entity<ProfileAttachment>()
                .HasOne(x => x.Profile)
                .WithMany(x => x.Attachments)
                .HasForeignKey(x => x.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Role>().HasData(
    new Role { Id = "a1000000-0000-0000-0000-000000000001", Name = AppRoles.Admin, NormalizedName = "ADMIN", ConcurrencyStamp = "role-admin-1" },
    new Role { Id = "a1000000-0000-0000-0000-000000000002", Name = AppRoles.Operator, NormalizedName = "OPERATOR", ConcurrencyStamp = "role-operator-1" },
    new Role { Id = "a1000000-0000-0000-0000-000000000003", Name = AppRoles.Viewer, NormalizedName = "VIEWER", ConcurrencyStamp = "role-viewer-1" }
);
        }
    }


}
