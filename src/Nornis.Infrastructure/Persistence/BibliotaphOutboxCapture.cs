using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Infrastructure.Persistence.Repositories;

namespace Nornis.Infrastructure.Persistence;

/// <summary>
/// Writes the Bibliotaph outbox rows (feature 31) for the changes Bibliotaph must hear about: a
/// world created or renamed, and an exposed upload arriving. The rows go into the same save as
/// the change, so an event is never lost to a crash between the two and never sent for a change
/// that rolled back.
///
/// <para>
/// Here, in the save path, for the reason <see cref="SlugAssigner"/> is: worlds are created by
/// the world create, the demo clone, the import and the template copy, and renamed by settings;
/// uploads are confirmed by the Library and filed by Prep's print. The next writer would not know
/// to call anything.
/// </para>
///
/// <para>
/// The change tracker can't be trusted to say what changed. Repositories save detached entities
/// with <c>Update</c>, which marks every property modified with the original value equal to the
/// new one, so "was the name modified?" is always yes and "what was it?" is always the new name.
/// So the old values are read from the database, and only for worlds whose creator is connected:
/// one query for the worlds and one for the connections when anything relevant is tracked, and
/// one more for upload statuses when an exposed upload is modified.
/// </para>
///
/// <para>
/// Deletes are not seen here. The world graph and the document delete use <c>ExecuteDelete</c>,
/// which the tracker never sees, so those paths call <see cref="QueueAsync"/> themselves.
/// </para>
/// </summary>
internal static class BibliotaphOutboxCapture
{
    private sealed record WorldFacts(Guid Id, string Name, Guid CreatedByUserId, bool IsDemo, bool IsTemplate)
    {
        public bool IsEligible => !IsDemo && !IsTemplate;
    }

    public static async Task CaptureAsync(NornisDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var worldEntries = db.ChangeTracker.Entries<World>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .ToList();
        var documentEntries = db.ChangeTracker.Entries<LibraryDocument>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .Where(e => BibliotaphExposure.IsExposed(e.Entity))
            .ToList();

        if (worldEntries.Count == 0 && documentEntries.Count == 0)
        {
            return;
        }

        // What each world is now: the tracked entity when there is one, else the database row.
        var current = worldEntries.ToDictionary(
            e => e.Entity.Id,
            e => new WorldFacts(e.Entity.Id, e.Entity.Name, e.Entity.CreatedByUserId, e.Entity.IsDemo, e.Entity.IsTemplate));
        var addedWorldIds = worldEntries.Where(e => e.State == EntityState.Added).Select(e => e.Entity.Id).ToHashSet();

        var storedIds = worldEntries.Where(e => e.State == EntityState.Modified).Select(e => e.Entity.Id)
            .Concat(documentEntries.Select(e => e.Entity.WorldId))
            .Where(id => !addedWorldIds.Contains(id))
            .Distinct()
            .ToList();
        var stored = await LoadWorldsAsync(db, storedIds, ct);
        foreach (var (id, facts) in stored)
        {
            current.TryAdd(id, facts);
        }

        var creators = current.Values.Where(w => w.IsEligible).Select(w => w.CreatedByUserId).Distinct().ToList();
        if (creators.Count == 0)
        {
            return;
        }

        var connected = (await db.BibliotaphConnections.AsNoTracking()
            .Where(c => creators.Contains(c.UserId))
            .Select(c => c.UserId)
            .ToListAsync(ct)).ToHashSet();
        if (connected.Count == 0)
        {
            return;
        }

        bool Delivers(Guid worldId) =>
            current.TryGetValue(worldId, out var w) && w.IsEligible && connected.Contains(w.CreatedByUserId);

        var events = new HashSet<(Guid WorldId, BibliotaphWorldEventKind Kind)>();

        foreach (var entry in worldEntries.Where(e => Delivers(e.Entity.Id)))
        {
            if (entry.State == EntityState.Added)
            {
                events.Add((entry.Entity.Id, BibliotaphWorldEventKind.Created));
            }
            else if (stored.TryGetValue(entry.Entity.Id, out var before)
                && !string.Equals(before.Name, entry.Entity.Name, StringComparison.Ordinal))
            {
                events.Add((entry.Entity.Id, BibliotaphWorldEventKind.Renamed));
            }
        }

        var relevantDocuments = documentEntries.Where(e => Delivers(e.Entity.WorldId)).ToList();
        foreach (var entry in relevantDocuments.Where(e => e.State == EntityState.Added))
        {
            events.Add((entry.Entity.WorldId, BibliotaphWorldEventKind.FilesChanged));
        }

        // A modified upload is news only when it has just left PendingUpload: the confirm.
        // Indexing's status moves and a GM's rename change nothing Bibliotaph lists.
        var modifiedIds = relevantDocuments.Where(e => e.State == EntityState.Modified).Select(e => e.Entity.Id).ToList();
        if (modifiedIds.Count > 0)
        {
            var wasPending = await db.LibraryDocuments.AsNoTracking()
                .Where(d => modifiedIds.Contains(d.Id) && d.Status == LibraryDocumentStatus.PendingUpload)
                .Select(d => d.WorldId)
                .ToListAsync(ct);
            foreach (var worldId in wasPending)
            {
                events.Add((worldId, BibliotaphWorldEventKind.FilesChanged));
            }
        }

        foreach (var (worldId, kind) in events)
        {
            var world = current[worldId];
            db.BibliotaphWorldEvents.Add(BibliotaphOutboxEvents.New(world.CreatedByUserId, worldId, world.Name, kind, now));
        }
    }

    /// <summary>
    /// Stages one event for a world, read from the database, when its creator is connected and it
    /// is neither a demo nor a template. The caller saves. Returns whether a row was staged.
    /// </summary>
    public static async Task<bool> QueueAsync(
        NornisDbContext db, Guid worldId, BibliotaphWorldEventKind kind, DateTimeOffset now, CancellationToken ct)
    {
        var worlds = await LoadWorldsAsync(db, [worldId], ct);
        if (!worlds.TryGetValue(worldId, out var world) || !world.IsEligible)
        {
            return false;
        }

        var connected = await db.BibliotaphConnections.AsNoTracking().AnyAsync(c => c.UserId == world.CreatedByUserId, ct);
        if (!connected)
        {
            return false;
        }

        db.BibliotaphWorldEvents.Add(BibliotaphOutboxEvents.New(world.CreatedByUserId, worldId, world.Name, kind, now));
        return true;
    }

    private static async Task<Dictionary<Guid, WorldFacts>> LoadWorldsAsync(
        NornisDbContext db, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Worlds.AsNoTracking()
            .Where(w => ids.Contains(w.Id))
            .Select(w => new WorldFacts(w.Id, w.Name, w.CreatedByUserId, w.IsDemo, w.IsTemplate))
            .ToDictionaryAsync(w => w.Id, ct);
    }
}
