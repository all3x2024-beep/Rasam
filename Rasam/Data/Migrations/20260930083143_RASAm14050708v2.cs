using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasam.Data.Migrations
{
    /// <inheritdoc />
    public partial class RASAm14050708v2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProfileRelation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FromProfileId = table.Column<int>(type: "int", nullable: false),
                    ToProfileId = table.Column<int>(type: "int", nullable: false),
                    RelationId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileRelation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileRelation_Profile_FromProfileId",
                        column: x => x.FromProfileId,
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProfileRelation_Profile_ToProfileId",
                        column: x => x.ToProfileId,
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProfileRelation_Relation_RelationId",
                        column: x => x.RelationId,
                        principalTable: "Relation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileRelation_FromProfileId_ToProfileId_RelationId",
                table: "ProfileRelation",
                columns: new[] { "FromProfileId", "ToProfileId", "RelationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProfileRelation_RelationId",
                table: "ProfileRelation",
                column: "RelationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileRelation_ToProfileId",
                table: "ProfileRelation",
                column: "ToProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProfileRelation");
        }
    }
}
