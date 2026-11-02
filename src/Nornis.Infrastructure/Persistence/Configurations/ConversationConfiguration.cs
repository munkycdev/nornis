using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversations");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(c => c.CreatedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(c => c.UpdatedAt)
            .HasColumnType("datetimeoffset");

        builder.HasIndex(c => c.WorldId);

        // One side conversation per Adventure. Filtered, because a conversation that isn't
        // Prep (null AdventureId) is allowed by the domain model and needs no partner.
        builder.HasIndex(c => c.AdventureId)
            .IsUnique()
            .HasFilter("[AdventureId] IS NOT NULL");

        builder.HasOne(c => c.World)
            .WithMany()
            .HasForeignKey(c => c.WorldId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, because Worlds already cascades to Adventures and SQL Server refuses a
        // second path. AdventureRepository.DeleteAsync deletes the conversation first.
        builder.HasOne(c => c.Adventure)
            .WithMany()
            .HasForeignKey(c => c.AdventureId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
