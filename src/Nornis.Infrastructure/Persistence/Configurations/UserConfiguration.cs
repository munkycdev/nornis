using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Auth0SubjectId)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(u => u.Username)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(u => u.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(u => u.UpdatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(u => u.RowVersion)
            .IsRowVersion();

        builder.HasIndex(u => u.Auth0SubjectId)
            .IsUnique();
    }
}
