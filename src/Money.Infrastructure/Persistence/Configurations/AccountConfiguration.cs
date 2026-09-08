using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Money.Domain.Accounts;

namespace Money.Infrastructure.Persistence.Configurations;

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Accounts", table =>
        {
            // Spec 5.2, enforced in the domain AND here.
            table.HasCheckConstraint("CK_Accounts_KindRole",
                "(Role = 'Category' AND Kind IN ('Income','Expense')) OR " +
                "(Role IN ('Bank','Cash','SavingsPocket','Investment') AND Kind = 'Asset') OR " +
                "(Role IN ('OpeningBalance','Adjustment') AND Kind = 'Equity')");

            table.HasCheckConstraint("CK_Accounts_PathShape", "Path LIKE '/%'");
        });

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name).HasMaxLength(Account.MaxNameLength).IsRequired();
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(a => a.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.Path).HasMaxLength(1024).IsRequired();
        builder.Property(a => a.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(a => a.ColorHex).HasMaxLength(7);
        builder.Property(a => a.Icon).HasMaxLength(64);
        builder.Property(a => a.Notes).HasMaxLength(2000);

        builder.HasOne<Account>()
               .WithMany()
               .HasForeignKey(a => a.ParentAccountId)
               .OnDelete(DeleteBehavior.Restrict);

        // Both uniqueness rules apply to the living tree only. A deleted account keeps its name and
        // path so old ledger entries can still be read, and must not thereby reserve that name
        // forever - deleting "Groceries" has to leave the user free to create "Groceries" again.
        builder.HasIndex(a => a.Path).IsUnique().HasFilter("IsDeleted = 0");
        builder.HasIndex(a => new { a.ParentAccountId, a.Name }).IsUnique().HasFilter("IsDeleted = 0");
        builder.HasIndex(a => new { a.Kind, a.Role });
    }
}
