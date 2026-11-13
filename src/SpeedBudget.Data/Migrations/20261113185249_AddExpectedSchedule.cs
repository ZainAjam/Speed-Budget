using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpeedBudget.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExpectedSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AnchorDay",
                table: "Expected",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "Expected",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Repeat",
                table: "Expected",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnchorDay",
                table: "Expected");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Expected");

            migrationBuilder.DropColumn(
                name: "Repeat",
                table: "Expected");
        }
    }
}
