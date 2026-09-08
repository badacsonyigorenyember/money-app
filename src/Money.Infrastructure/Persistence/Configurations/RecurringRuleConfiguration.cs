using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Money.Domain.Recurrence;

namespace Money.Infrastructure.Persistence.Configurations;

public sealed class RecurringRuleConfiguration : IEntityTypeConfiguration<RecurringRule>
{
    public void Configure(EntityTypeBuilder<RecurringRule> builder)
    {
        builder.ToTable("RecurringRules");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Description).HasMaxLength(500).IsRequired();
        builder.Property(r => r.Payee).HasMaxLength(200);
        builder.Property(r => r.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();

        // The schedule is part of the rule, not a thing of its own: it has no identity, is never
        // queried alone, and dies with the rule. Owned, so it lives in the same row.
        builder.OwnsOne(r => r.Schedule, schedule =>
        {
            schedule.Property(s => s.Frequency)
                    .HasColumnName("Frequency").HasConversion<string>().HasMaxLength(16).IsRequired();

            schedule.Property(s => s.Interval).HasColumnName("Interval").IsRequired();
            schedule.Property(s => s.DayOfWeek).HasColumnName("DayOfWeek");
            schedule.Property(s => s.WeekOfMonth).HasColumnName("WeekOfMonth");
            schedule.Property(s => s.DayOfMonth).HasColumnName("DayOfMonth");
            schedule.Property(s => s.Month).HasColumnName("Month");
            schedule.Property(s => s.CustomYears).HasColumnName("CustomYears").IsRequired();
            schedule.Property(s => s.CustomMonths).HasColumnName("CustomMonths").IsRequired();
            schedule.Property(s => s.CustomDays).HasColumnName("CustomDays").IsRequired();
        });

        builder.Navigation(r => r.Schedule).IsRequired();

        // The amount is a magnitude carried by both sides, so zero would mean a rule that posts
        // nothing at all - the same check Transaction makes for a posting.
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_RecurringRules_AmountMinor_NonZero", "AmountMinor <> 0"));

        builder.HasIndex(r => r.IsActive);
    }
}
