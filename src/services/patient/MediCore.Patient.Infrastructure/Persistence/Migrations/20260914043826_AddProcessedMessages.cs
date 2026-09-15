using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCore.Patient.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessedMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "processed_messages",
                schema: "medicore_patient",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ConsumerGroup = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SourceTopic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_messages", x => x.MessageId);
                });

            migrationBuilder.CreateIndex(
                name: "ix_processed_messages_consumer_processed_at",
                schema: "medicore_patient",
                table: "processed_messages",
                columns: new[] { "ConsumerGroup", "ProcessedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processed_messages",
                schema: "medicore_patient");
        }
    }
}
