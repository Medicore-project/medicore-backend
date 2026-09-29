using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCore.Appointment.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// SCRUM-35: maps the slot's optimistic concurrency token to Postgres's <c>xmin</c> system
    /// column.
    /// </summary>
    /// <remarks>
    /// The <c>AddColumn</c> below records the mapping in the model snapshot only. <c>xmin</c>
    /// exists on every Postgres table already, and Npgsql's SQL generator emits no DDL for it —
    /// <c>dotnet ef migrations script</c> for this migration writes nothing but the history row.
    /// </remarks>
    public partial class AddSlotConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "medicore_appointment",
                table: "slots",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "medicore_appointment",
                table: "slots");
        }
    }
}
