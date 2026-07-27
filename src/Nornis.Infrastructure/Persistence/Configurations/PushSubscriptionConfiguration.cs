using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.ToTable("PushSubscriptions");

        builder.HasKey(s => s.Id);

        // Push endpoints are long URLs; 512 is comfortable for every service in the wild while
        // staying short enough to index.
        builder.Property(s => s.Endpoint).IsRequired().HasMaxLength(512);
        builder.Property(s => s.P256dh).IsRequired().HasMaxLength(256);
        builder.Property(s => s.Auth).IsRequired().HasMaxLength(256);
        builder.Property(s => s.Label).HasMaxLength(200);

        // The endpoint IS the browser's identity: a re-subscribe must update in place rather
        // than accumulate rows that every later send fails against.
        builder.HasIndex(s => s.Endpoint).IsUnique();

        // Every send starts by asking which browsers a person has.
        builder.HasIndex(s => s.UserId);

        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
