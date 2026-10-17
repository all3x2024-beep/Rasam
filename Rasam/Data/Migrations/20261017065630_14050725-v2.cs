using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasam.Data.Migrations
{
    /// <inheritdoc />
    public partial class _14050725v2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Profile_UsernameId",
                table: "Profile");

            migrationBuilder.AlterColumn<string>(
                name: "UsernameId",
                table: "Profile",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.UpdateData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "a1000000-0000-0000-0000-000000000002",
                columns: new[] { "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[] { "role-editor-1", "Editor", "EDITOR" });

            migrationBuilder.CreateIndex(
                name: "IX_Profile_UsernameId",
                table: "Profile",
                column: "UsernameId",
                unique: true,
                filter: "[UsernameId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Profile_UsernameId",
                table: "Profile");

            migrationBuilder.AlterColumn<string>(
                name: "UsernameId",
                table: "Profile",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "a1000000-0000-0000-0000-000000000002",
                columns: new[] { "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[] { "role-operator-1", "Operator", "OPERATOR" });

            migrationBuilder.CreateIndex(
                name: "IX_Profile_UsernameId",
                table: "Profile",
                column: "UsernameId",
                unique: true);
        }
    }
}
