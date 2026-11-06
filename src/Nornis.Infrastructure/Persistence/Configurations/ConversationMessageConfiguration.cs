using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;
using Nornis.Domain.Models;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class ConversationMessageConfiguration : IEntityTypeConfiguration<ConversationMessage>
{
    private static readonly JsonSerializerOptions WebResearchJson = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<ConversationMessage> builder)
    {
        builder.ToTable("ConversationMessages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Role)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        // Bounded in AdventureConversationService, not by the column: PrepOptions'
        // MaxConversationMessageChars for the GM's, the output ceiling for an answer.
        builder.Property(m => m.Content)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.Property(m => m.Model)
            .HasMaxLength(200);

        // Feature 40: one JSON document, read and written whole with its message and never
        // queried. Bounded by WebResearchResult.Create (20 pages, 32 KB of suggestions), not by
        // the column. Compared by its JSON, since the record's page list compares by reference.
        builder.Property(m => m.WebResearch)
            .HasColumnName("WebResearchJson")
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => JsonSerializer.Serialize(v, WebResearchJson),
                s => JsonSerializer.Deserialize<WebResearchResult>(s, WebResearchJson),
                new ValueComparer<WebResearchResult?>(
                    (a, b) => JsonSerializer.Serialize(a, WebResearchJson) == JsonSerializer.Serialize(b, WebResearchJson),
                    v => JsonSerializer.Serialize(v, WebResearchJson).GetHashCode(StringComparison.Ordinal),
                    v => v));

        builder.Property(m => m.CreatedAt)
            .HasColumnType("datetimeoffset");

        // The panel and the history window both read a conversation's messages in time order.
        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt });

        builder.HasOne(m => m.Conversation)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
