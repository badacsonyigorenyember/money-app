using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Money.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceWeekOfMonthWithMoveOffWeekends : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // "The first Monday of the month" is gone, and a monthly or yearly rule left with no
            // day number would expand to a null deref. Any such rule keeps the day its start date
            // names - the closest thing to the user's intent still on the row. This runs before
            // the drop because it reads WeekOfMonth.
            migrationBuilder.Sql(
                """
                UPDATE RecurringRules
                   SET DayOfMonth = CAST(strftime('%d', StartDate) AS INTEGER)
                 WHERE DayOfMonth IS NULL
                   AND WeekOfMonth IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "WeekOfMonth",
                table: "RecurringRules");

            migrationBuilder.AddColumn<bool>(
                name: "MoveOffWeekends",
                table: "RecurringRules",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MoveOffWeekends",
                table: "RecurringRules");

            migrationBuilder.AddColumn<int>(
                name: "WeekOfMonth",
                table: "RecurringRules",
                type: "INTEGER",
                nullable: true);
        }
    }
}
