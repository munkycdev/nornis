namespace Nornis.Domain.Entities;

/// <summary>
/// An entity with a detail page, addressed by a friendly slug rather than its id.
///
/// The slug is assigned once, from <see cref="SlugSource"/> at creation, and does not follow a
/// rename: it is a permalink, like the world's public slug. Following the name would let an old
/// link land on whatever entity later took the freed slug, which is worse than a stale word in
/// the address bar. Unique per world; null only for rows written before slugs existed and not
/// yet backfilled, which every link builder treats as "use the id".
///
/// Assignment lives in the persistence layer (<c>SlugAssigner</c>) so that every writer gets it
/// without knowing: review apply, the demo clone, world import, uploads.
/// </summary>
public interface ISlugged
{
    Guid Id { get; }

    Guid WorldId { get; }

    string? Slug { get; set; }

    /// <summary>The display text a slug is derived from: the name, or a title where that is the word used.</summary>
    string SlugSource { get; }
}
