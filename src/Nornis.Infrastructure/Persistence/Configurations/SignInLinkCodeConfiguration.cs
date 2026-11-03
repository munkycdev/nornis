using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class SignInLinkCodeConfiguration : IEntityTypeConfiguration<SignInLinkCode>
{
    public void Configure(EntityTypeBuilder<SignInLinkCode> builder)
    {
        builder.ToTable("SignInLinkCodes");

        // One outstanding code per user: the key is what bounds the table.
        builder.HasKey(c => c.UserId);

        builder.Property(c => c.CodeHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.ExpiresAt)
            .HasColumnType("datetimeoffset");

        builder.HasIndex(c => c.CodeHash)
            .IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
