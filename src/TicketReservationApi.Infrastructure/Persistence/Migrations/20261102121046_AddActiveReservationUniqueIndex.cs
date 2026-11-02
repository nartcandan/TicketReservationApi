using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketReservationApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActiveReservationUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reservations_EventId_UserId",
                table: "Reservations");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_EventId_UserId",
                table: "Reservations",
                columns: new[] { "EventId", "UserId" },
                unique: true,
                filter: "[Status] = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reservations_EventId_UserId",
                table: "Reservations");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_EventId_UserId",
                table: "Reservations",
                columns: new[] { "EventId", "UserId" });
        }
    }
}
