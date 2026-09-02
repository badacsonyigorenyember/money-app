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
            // Spec 5.2, enforced in the domain AND here. Kind and Role are stored as their
            // integer enum values (AccountKind: Asset=1, Liability=2, Income=3, Expense=4,
            // Equity=5; AccountRole: Bank=1, Cash=2, SavingsPocket=3, Investment=4, Category=5,
            // OpeningBalance=6, Adjustment=7) - the domain's enum numbers are a storage contract,
            // so this check is written in those numbers rather than strings.
            table.HasCheckConstraint("CK_Accounts_KindRole",
                "(Role = 5 AND Kind IN (3, 4)) OR " +
                "(Role IN (1, 2, 3, 4) AND Kind = 1) OR " +
                "(Role IN (6, 7) AND Kind = 5)");

            table.HasCheckConstraint("CK_Accounts_PathShape", "Path LIKE '/%'");
        });

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name).HasMaxLength(Account.MaxNameLength).IsRequired();
        builder.Property(a => a.Kind).HasColumnType("INTEGER").IsRequired();
        builder.Property(a => a.Role).HasColumnType("INTEGER").IsRequired();
        builder.Property(a => a.Path).HasMaxLength(1024).IsRequired();
        builder.Property(a => a.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(a => a.ColorHex).HasMaxLength(7);
        builder.Property(a => a.Icon).HasMaxLength(64);
        builder.Property(a => a.Notes).HasMaxLength(2000);

        builder.HasOne<Account>()
               .WithMany()
               .HasForeignKey(a => a.ParentAccountId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.Path).IsUnique();
        builder.HasIndex(a => new { a.ParentAccountId, a.Name }).IsUnique();
        builder.HasIndex(a => new { a.Kind, a.Role });
    }
}
