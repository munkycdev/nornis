using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nornis.Domain.Entities;

namespace Nornis.Infrastructure.Persistence.Configurations;

public class CheckInQuestionConfiguration : IEntityTypeConfiguration<CheckInQuestion>
{
    public void Configure(EntityTypeBuilder<CheckInQuestion> builder)
    {
        builder.ToTable("CheckInQuestions");

        builder.HasKey(q => q.Id);

        builder.Property(q => q.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(q => q.SubjectLabel)
            .IsRequired()
            .HasMaxLength(CheckInQuestion.MaxLabelChars);

        builder.Property(q => q.Text)
            .IsRequired()
            .HasMaxLength(CheckInQuestion.MaxTextChars);

        builder.Property(q => q.Why)
            .HasMaxLength(CheckInQuestion.MaxTextChars);

        builder.Property(q => q.EvidenceJson)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.Property(q => q.SuggestedRule)
            .HasMaxLength(EditorialRule.MaxTextChars);

        builder.Property(q => q.Answer)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(q => q.AnsweredAt)
            .HasColumnType("datetimeoffset");

        builder.Ignore(q => q.IsAnswered);

        builder.HasIndex(q => new { q.CheckInId, q.Order });
    }
}
