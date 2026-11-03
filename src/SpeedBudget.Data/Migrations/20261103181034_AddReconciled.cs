using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpeedBudget.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsReconciled",
                table: "Transactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsReconciled",
                table: "Transactions");
        }
    }
}
