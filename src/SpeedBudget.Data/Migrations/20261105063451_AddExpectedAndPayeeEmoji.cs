using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpeedBudget.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExpectedAndPayeeEmoji : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Emoji",
                table: "Payees",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Expected",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Payee = table.Column<string>(type: "TEXT", nullable: false),
                    Memo = table.Column<string>(type: "TEXT", nullable: true),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    BudgetItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ExpectedDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expected", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Expected_AccountId",
                table: "Expected",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Expected_UpdatedAt",
                table: "Expected",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Expected");

            migrationBuilder.DropColumn(
                name: "Emoji",
                table: "Payees");
        }
    }
}
