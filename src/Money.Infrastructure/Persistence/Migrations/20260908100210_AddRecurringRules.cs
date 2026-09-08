using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Money.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecurringRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Payee = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    DebitAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreditAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrencyCode = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    Frequency = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Interval = table.Column<int>(type: "INTEGER", nullable: false),
                    DayOfWeek = table.Column<int>(type: "INTEGER", nullable: true),
                    WeekOfMonth = table.Column<int>(type: "INTEGER", nullable: true),
                    DayOfMonth = table.Column<int>(type: "INTEGER", nullable: true),
                    Month = table.Column<int>(type: "INTEGER", nullable: true),
                    CustomYears = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomMonths = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomDays = table.Column<int>(type: "INTEGER", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastMaterialisedThrough = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringRules", x => x.Id);
                    table.CheckConstraint("CK_RecurringRules_AmountMinor_NonZero", "AmountMinor <> 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringRules_IsActive",
                table: "RecurringRules",
                column: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecurringRules");
        }
    }
}
