using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixSoftDeleteUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_specializations_Name",
                schema: "medicore_identity",
                table: "specializations");

            migrationBuilder.DropIndex(
                name: "IX_departments_Name",
                schema: "medicore_identity",
                table: "departments");

            migrationBuilder.CreateIndex(
                name: "IX_specializations_Name",
                schema: "medicore_identity",
                table: "specializations",
                column: "Name",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_departments_Name",
                schema: "medicore_identity",
                table: "departments",
                column: "Name",
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_specializations_Name",
                schema: "medicore_identity",
                table: "specializations");

            migrationBuilder.DropIndex(
                name: "IX_departments_Name",
                schema: "medicore_identity",
                table: "departments");

            migrationBuilder.CreateIndex(
                name: "IX_specializations_Name",
                schema: "medicore_identity",
                table: "specializations",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_departments_Name",
                schema: "medicore_identity",
                table: "departments",
                column: "Name",
                unique: true);
        }
    }
}
