using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Money.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ParentAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Path = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    CurrencyCode = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    OpenedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ColorHex = table.Column<string>(type: "TEXT", maxLength: 7, nullable: true),
                    Icon = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                    table.CheckConstraint("CK_Accounts_KindRole", "(Role = 'Category' AND Kind IN ('Income','Expense')) OR (Role IN ('Bank','Cash','SavingsPocket','Investment') AND Kind = 'Asset') OR (Role IN ('OpeningBalance','Adjustment') AND Kind = 'Equity')");
                    table.CheckConstraint("CK_Accounts_PathShape", "Path LIKE '/%'");
                    table.ForeignKey(
                        name: "FK_Accounts_Accounts_ParentAccountId",
                        column: x => x.ParentAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ResponseJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => new { x.Key, x.Endpoint });
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    BaseCurrencyCode = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    PeriodAnchor = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PeriodAnchorDay = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FirstDayOfWeek = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    BackupRetentionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstRunCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    WindowWidth = table.Column<int>(type: "INTEGER", nullable: true),
                    WindowHeight = table.Column<int>(type: "INTEGER", nullable: true),
                    WindowX = table.Column<int>(type: "INTEGER", nullable: true),
                    WindowY = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                    table.CheckConstraint("CK_Settings_SingleRow", "Id = 1");
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    BookedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Payee = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ExternalRef = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    IsVoided = table.Column<bool>(type: "INTEGER", nullable: false),
                    VoidedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    VoidReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Postings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TransactionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrencyCode = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    Memo = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Postings", x => x.Id);
                    table.CheckConstraint("CK_Postings_NonZero", "AmountMinor <> 0");
                    table.ForeignKey(
                        name: "FK_Postings_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Postings_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Kind_Role",
                table: "Accounts",
                columns: new[] { "Kind", "Role" });

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

            migrationBuilder.CreateIndex(
                name: "IX_Postings_AccountId_TransactionId",
                table: "Postings",
                columns: new[] { "AccountId", "TransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Postings_Balance",
                table: "Postings",
                columns: new[] { "AccountId", "TransactionId", "AmountMinor", "CurrencyCode" });

            migrationBuilder.CreateIndex(
                name: "IX_Postings_TransactionId",
                table: "Postings",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_OccurredOn",
                table: "Transactions",
                column: "OccurredOn");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_SourceKind_SourceId_OccurredOn",
                table: "Transactions",
                columns: new[] { "SourceKind", "SourceId", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "UX_Transactions_Source_Idempotency",
                table: "Transactions",
                columns: new[] { "SourceKind", "SourceId", "OccurredOn" },
                unique: true,
                filter: "SourceKind IN ('Recurring','Accrual') AND SourceId IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdempotencyRecords");

            migrationBuilder.DropTable(
                name: "Postings");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "Transactions");
        }
    }
}
