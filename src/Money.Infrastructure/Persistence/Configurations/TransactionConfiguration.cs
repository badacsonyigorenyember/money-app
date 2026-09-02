using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Money.Domain.Ledger;

namespace Money.Infrastructure.Persistence.Configurations;

public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Description).HasMaxLength(500).IsRequired();
        builder.Property(t => t.Payee).HasMaxLength(200);
        builder.Property(t => t.ExternalRef).HasMaxLength(200);
        builder.Property(t => t.VoidReason).HasMaxLength(500);

        builder.Property(t => t.SourceKind).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.HasMany(t => t.Postings)
               .WithOne()
               .HasForeignKey(p => p.TransactionId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(t => t.Postings)
               .HasField("_postings")
               .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(t => t.OccurredOn);

        // Plain lookup index over the same three columns as the idempotency guard below. The name
        // must be passed to HasIndex itself (not chained via HasDatabaseName afterwards): two
        // HasIndex calls over an identical property list resolve to the same IndexBuilder unless
        // they are disambiguated at the point of creation, and would otherwise collapse into one.
        builder.HasIndex(
            t => new { t.SourceKind, t.SourceId, t.OccurredOn },
            "IX_Transactions_SourceKind_SourceId_OccurredOn");

        // THE idempotency guard (spec section 8). Partial, so ordinary manual entries are
        // unaffected: two coffees on the same day must still be possible.
        builder.HasIndex(
                t => new { t.SourceKind, t.SourceId, t.OccurredOn },
                "UX_Transactions_Source_Idempotency")
               .IsUnique()
               .HasFilter("SourceKind IN ('Recurring','Accrual') AND SourceId IS NOT NULL");
    }
}
