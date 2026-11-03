using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class UserSignInConfiguration : IEntityTypeConfiguration<UserSignIn>
{
    public void Configure(EntityTypeBuilder<UserSignIn> builder)
    {
        builder.ToTable("UserSignIns");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Subject)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.AddedAt)
            .HasColumnType("datetimeoffset");

        // A subject belongs to at most one user; this is also what makes a create race fail
        // rather than produce two users for one subject.
        builder.HasIndex(s => s.Subject)
            .IsUnique();

        builder.HasIndex(s => s.UserId);
    }
}
