using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Money.Domain.Accounts;
using Money.Domain.Ledger;

namespace Money.Infrastructure.Persistence.Configurations;

public sealed class PostingConfiguration : IEntityTypeConfiguration<Posting>
{
    public void Configure(EntityTypeBuilder<Posting> builder)
    {
        builder.ToTable("Postings", table =>
            table.HasCheckConstraint("CK_Postings_NonZero", "AmountMinor <> 0"));

        builder.HasKey(p => p.Id);

        builder.Property(p => p.AmountMinor).HasColumnType("INTEGER").IsRequired();
        builder.Property(p => p.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(p => p.Memo).HasMaxLength(500);

        // A posting must reference a real account row. This is separate from I4 (no posting
        // against an *archived* account, a domain rule) - this FK only rules out a dangling
        // reference to an account that does not exist at all. Restrict, not Cascade: deletes do
        // not exist in this system, and Restrict is what stops an account row deletion (which
        // never happens in application code - accounts are archived, not deleted) from silently
        // taking ledger history with it.
        builder.HasOne<Account>()
               .WithMany()
               .HasForeignKey(p => p.AccountId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.AccountId, p.TransactionId });

        // Covering index for balance queries. SQLite has no INCLUDE clause, so the covering
        // effect comes from a plain composite index whose columns suffice for the query
        // (AccountId, then everything a balance read needs) without touching the base table.
        builder.HasIndex(p => new { p.AccountId, p.TransactionId, p.AmountMinor, p.CurrencyCode })
               .HasDatabaseName("IX_Postings_Balance");
    }
}
