using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRolesAndUserRolesWithSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "roles",
                schema: "medicore_identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                schema: "medicore_identity",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_roles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_user_roles_roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "medicore_identity",
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_roles_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "medicore_identity",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "medicore_identity",
                table: "roles",
                columns: new[] { "Id", "Description", "Name" },
                values: new object[,]
                {
                    { new Guid("a1a1a1a1-0000-0000-0000-000000000001"), "Full system access", "Admin" },
                    { new Guid("a1a1a1a1-0000-0000-0000-000000000002"), "Clinical staff with patient access", "Doctor" },
                    { new Guid("a1a1a1a1-0000-0000-0000-000000000003"), "Nursing staff with patient access", "Nurse" },
                    { new Guid("a1a1a1a1-0000-0000-0000-000000000004"), "Front-desk appointment management", "Receptionist" },
                    { new Guid("a1a1a1a1-0000-0000-0000-000000000005"), "Patient portal access", "Patient" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_roles_Name",
                schema: "medicore_identity",
                table: "roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_RoleId",
                schema: "medicore_identity",
                table: "user_roles",
                column: "RoleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_roles",
                schema: "medicore_identity");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "medicore_identity");
        }
    }
}
