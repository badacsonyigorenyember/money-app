using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Money.Infrastructure.Persistence.Configurations;

public sealed class SettingsConfiguration : IEntityTypeConfiguration<SettingsEntity>
{
    public void Configure(EntityTypeBuilder<SettingsEntity> builder)
    {
        builder.ToTable("Settings", table =>
            table.HasCheckConstraint("CK_Settings_SingleRow", "Id = 1"));

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.BaseCurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(s => s.PeriodAnchor).HasMaxLength(20).IsRequired();
        builder.Property(s => s.TimeZoneId).HasMaxLength(64).IsRequired();
        builder.Property(s => s.FirstDayOfWeek).HasMaxLength(10).IsRequired();
    }
}
