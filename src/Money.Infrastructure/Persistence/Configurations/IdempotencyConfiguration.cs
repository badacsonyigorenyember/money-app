using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Money.Infrastructure.Persistence.Configurations;

public sealed class IdempotencyConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");

        builder.HasKey(r => new { r.Key, r.Endpoint });

        builder.Property(r => r.Key).HasMaxLength(200);
        builder.Property(r => r.Endpoint).HasMaxLength(200);
        builder.Property(r => r.RequestBodyHash).IsRequired().HasMaxLength(64);
        builder.Property(r => r.ResponseJson);
    }
}
