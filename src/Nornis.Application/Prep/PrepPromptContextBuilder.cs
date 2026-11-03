using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Knowledge;
using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Prep;

/// <summary>
/// Builds <see cref="PrepPromptContext"/> (feature 37): the one place a Prep call gathers the
/// world's record and the reference passages. Both are read at the GM's role (Prep is GM-only, so
/// Hidden truth and GM-only books are in), and the passages come from the Adventure's source books
/// when it has chosen some, or from every book otherwise.
/// </summary>
public class PrepPromptContextBuilder
{
    private readonly IWorldRepository _worlds;
    private readonly IKnowledgeRetriever _knowledge;
    private readonly IReferencePassageRetriever _references;
    private readonly LoremasterOptions _loremaster;

    public PrepPromptContextBuilder(
        IWorldRepository worlds, IKnowledgeRetriever knowledge, IReferencePassageRetriever references,
        IOptions<LoremasterOptions> loremaster)
    {
        _worlds = worlds;
        _knowledge = knowledge;
        _references = references;
        _loremaster = loremaster.Value;
    }

    public async Task<PrepPromptContext> BuildAsync(
        Adventure adventure, string retrievalText, Guid userId, WorldRole role, CancellationToken ct)
    {
        var world = await _worlds.GetByIdAsync(adventure.WorldId, ct);
        var context = await _knowledge.RetrieveAsync(retrievalText, adventure.WorldId, userId, role, ct);

        var books = adventure.SourceBooks.Select(b => b.LibraryDocumentId).ToList();
        var found = books.Count > 0
            ? await _references.RetrieveFromDocumentsAsync(retrievalText, adventure.WorldId, userId, books, ct)
            : await _references.RetrieveAsync(retrievalText, adventure.WorldId, userId, role, ct);
        if (found.Passages.Count > 0)
        {
            context = new KnowledgeContext
            {
                Artifacts = context.Artifacts,
                Facts = context.Facts,
                Relationships = context.Relationships,
                SourceReferences = context.SourceReferences,
                Passages = found.Passages,
                Sessions = context.Sessions,
                ReferenceBooksUnavailable = found.Unavailable,
            };
        }

        return new PrepPromptContext(
            PrintDetails.SystemOf(adventure.Details, world?.GameSystem),
            adventure.Details.Levels,
            adventure.TableBrief,
            LoremasterService.FormatKnowledgeContext(context, _loremaster.MaxSessionChars, _loremaster.MaxContextTokens),
            found.Unavailable);
    }
}
