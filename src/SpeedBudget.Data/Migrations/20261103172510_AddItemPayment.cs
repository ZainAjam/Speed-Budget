using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpeedBudget.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddItemPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PaymentMethod",
                table: "Items",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReportHeading",
                table: "Items",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "ReportHeading",
                table: "Items");
        }
    }
}
