using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpeedBudget.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPinOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PinOrder",
                table: "Items",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PinOrder",
                table: "Items");
        }
    }
}
