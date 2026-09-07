using System;
using MediCore.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(IdentityDbContext))]
    [Migration("20260906120000_SeedBootstrapAdmin")]
    public partial class SeedBootstrapAdmin : Migration
    {
        // Dev-only bootstrap credentials — there is no other way to obtain an Admin
        // account once management endpoints require [Authorize(Policy = "AdminOnly")].
        // Login: admin@medicore.local / Admin@12345
        private const string AdministrationDepartmentId = "b1b1b1b1-0000-0000-0000-000000000001";
        private const string AdminUserId = "c1c1c1c1-0000-0000-0000-000000000001";
        private const string AdminStaffProfileId = "d1d1d1d1-0000-0000-0000-000000000001";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Raw SQL (not InsertData) because a hand-written data-only migration has
            // no BuildTargetModel() snapshot for EF to infer column types from.
            var passwordHash = BCrypt.Net.BCrypt.HashPassword("Admin@12345", workFactor: 12);

            migrationBuilder.Sql($"""
                INSERT INTO medicore_identity.departments
                    ("Id", "Name", "Description", "IsActive", "IsDeleted", "CreatedAt", "CreatedBy")
                VALUES
                    ('{AdministrationDepartmentId}', 'Administration', 'Hospital administration and IT', true, false, now(), 'system');
                """);

            migrationBuilder.Sql($"""
                INSERT INTO medicore_identity.users
                    ("Id", "Email", "PasswordHash", "Role", "IsActive", "IsDeleted", "CreatedAt", "CreatedBy")
                VALUES
                    ('{AdminUserId}', 'admin@medicore.local', '{passwordHash}', 'Admin', true, false, now(), 'system');
                """);

            migrationBuilder.Sql($"""
                INSERT INTO medicore_identity.staff_profiles
                    ("Id", "UserId", "FirstName", "LastName", "Phone", "Specialization", "DepartmentId", "HireDate", "IsActive", "IsDeleted", "CreatedAt", "CreatedBy")
                VALUES
                    ('{AdminStaffProfileId}', '{AdminUserId}', 'System', 'Administrator', '', '', '{AdministrationDepartmentId}', now(), true, false, now(), 'system');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DELETE FROM medicore_identity.staff_profiles WHERE \"Id\" = '{AdminStaffProfileId}';");
            migrationBuilder.Sql($"DELETE FROM medicore_identity.users WHERE \"Id\" = '{AdminUserId}';");
            migrationBuilder.Sql($"DELETE FROM medicore_identity.departments WHERE \"Id\" = '{AdministrationDepartmentId}';");
        }
    }
}
