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

        // Ids are always assigned by domain code (Guid.CreateVersion7), never by the database.
        // Without this, EF's default convention for Guid keys assumes database generation, so a
        // freshly-created Posting discovered only through a Replace()'d collection navigation
        // (never explicitly Added) is mistaken for an already-existing row and gets an UPDATE
        // instead of an INSERT - which then fails as a concurrency exception because no such row
        // exists yet.
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.AmountMinor).HasColumnType("INTEGER").IsRequired();
        builder.Property(p => p.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(p => p.Memo).HasMaxLength(500);

        // A posting must reference a real account row. This is separate from I4 (no posting
        // against an *archived* account, a domain rule) - this FK only rules out a dangling
        // reference to an account that does not exist at all. Restrict, not Cascade: an account
        // the user deletes is only erased when no posting refers to it, and Restrict is what makes
        // that safe - a bug that tried to erase a referenced account would be refused here rather
        // than silently taking ledger history with it.
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
