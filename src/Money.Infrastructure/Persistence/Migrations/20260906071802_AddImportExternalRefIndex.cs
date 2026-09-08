using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Money.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImportExternalRefIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_Transactions_Import_ExternalRef",
                table: "Transactions",
                column: "ExternalRef",
                unique: true,
                filter: "SourceKind = 'Import' AND ExternalRef IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Transactions_Import_ExternalRef",
                table: "Transactions");
        }
    }
}
