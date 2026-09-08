using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Money.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountIsDeleted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Accounts_ParentAccountId_Name",
                table: "Accounts");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_Path",
                table: "Accounts");

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_ParentAccountId_Name",
                table: "Accounts",
                columns: new[] { "ParentAccountId", "Name" },
                unique: true,
                filter: "IsDeleted = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Path",
                table: "Accounts",
                column: "Path",
                unique: true,
                filter: "IsDeleted = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Accounts_ParentAccountId_Name",
                table: "Accounts");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_Path",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Accounts");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_ParentAccountId_Name",
                table: "Accounts",
                columns: new[] { "ParentAccountId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Path",
                table: "Accounts",
                column: "Path",
                unique: true);
        }
    }
}
