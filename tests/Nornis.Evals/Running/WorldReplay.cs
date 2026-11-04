using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nornis.Application.Ai;
using Nornis.Evals.Corpus;
using Nornis.Evals.Scoring;

namespace Nornis.Evals.Running;

/// <summary>
/// The world each case is read against, rebuilt from proposals accepted on the cases before it:
/// the eval's stand-in for the record <c>ExtractionService</c> reads from the database. Every
/// model is given the same context (the reviewed baseline's), so their answers differ only by
/// the model. While the baseline itself is being made, each case's answer is accepted as it
/// comes (<see cref="AcceptAsRead"/>), as a GM who accepts everything would.
///
/// The context is chosen as production chooses it: artifacts named in the source first, then
/// the most recently changed, at most 50, each with its 20 latest facts, and a party-visible
/// source sees only party-visible artifacts. Ids are derived from names, so the same baseline
/// always gives the same ids and a run can be rescored later.
/// </summary>
public sealed class WorldReplay : IEvalContext
{
    public const int MaxArtifacts = 50;
    public const int MaxFacts = 20;

    private readonly Dictionary<string, List<IReadOnlyList<JsonElement>>> _accepted = new(StringComparer.Ordinal);
    private readonly bool _acceptAsRead;

    private WorldReplay(bool acceptAsRead) => _acceptAsRead = acceptAsRead;

    /// <summary>For making the baseline: every answer is accepted once read.</summary>
    public static WorldReplay AcceptAsRead() => new(acceptAsRead: true);

    /// <summary>For every other run: the reviewed baseline's kept proposals, per world, one list
    /// per case in corpus order.</summary>
    public static WorldReplay From(IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<JsonElement>>> kept)
    {
        var replay = new WorldReplay(acceptAsRead: false);
        foreach (var (world, cases) in kept)
        {
            replay._accepted[world] = cases.ToList();
        }

        return replay;
    }

    public IReadOnlyList<ArtifactContext> Before(EvalWorld world, int caseIndex)
    {
        var state = Replay(world.Id, caseIndex);
        var source = world.Cases[caseIndex];
        var partyOnly = source.Visibility == "PartyVisible";
        var visible = state.Artifacts.Values.Where(a => !partyOnly || a.Visibility is null or "PartyVisible").ToList();

        var named = visible.Where(a => source.Body.Contains(a.Name, StringComparison.OrdinalIgnoreCase));
        var recent = visible.OrderByDescending(a => a.Touched);
        return named.Concat(recent).DistinctBy(a => a.Id).Take(MaxArtifacts).Select(a => new ArtifactContext
        {
            Id = a.Id,
            Name = a.Name,
            Type = a.Type,
            Summary = a.Summary,
            Facts = a.Facts.Values.OrderByDescending(f => f.Touched).Take(MaxFacts)
                .Select(f => new FactContext { Id = f.Id, Predicate = f.Predicate, Value = f.Value }).ToList(),
            PartOfName = a.PartOf,
        }).ToList();
    }

    public void Read(EvalWorld world, int caseIndex, IReadOnlyList<JsonElement>? proposals)
    {
        if (!_acceptAsRead)
        {
            return;
        }

        var cases = _accepted.TryGetValue(world.Id, out var list) ? list : _accepted[world.Id] = [];
        while (cases.Count <= caseIndex)
        {
            cases.Add([]);
        }

        cases[caseIndex] = proposals ?? [];
    }

    /// <summary>Every id the replay hands out in <paramref name="world"/>, artifacts and facts
    /// (<c>Artifact · predicate</c>), so a proposal that points at one by id can be read by name.</summary>
    public IReadOnlyDictionary<Guid, string> Names(string world)
    {
        var state = Replay(world, int.MaxValue);
        var names = new Dictionary<Guid, string>();
        foreach (var artifact in state.Artifacts.Values)
        {
            names[artifact.Id] = artifact.Name;
            foreach (var fact in artifact.Facts.Values)
            {
                names[fact.Id] = $"{artifact.Name} · {fact.Predicate}";
            }
        }

        foreach (var (id, name) in state.Gone)
        {
            names.TryAdd(id, name);
        }

        return names;
    }

    private ReplayState Replay(string world, int beforeCase)
    {
        var state = new ReplayState(world);
        if (_accepted.TryGetValue(world, out var cases))
        {
            for (var i = 0; i < Math.Min(beforeCase, cases.Count); i++)
            {
                state.Accept(cases[i], i);
            }
        }

        return state;
    }

    internal static Guid IdFor(string world, string kind, string key) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes($"{world}|{kind}|{key}")));

    private sealed class ReplayState(string world)
    {
        public Dictionary<string, ArtifactState> Artifacts { get; } = new(StringComparer.Ordinal);

        /// <summary>Artifacts merged away, still named so a late reference reads.</summary>
        public Dictionary<Guid, string> Gone { get; } = [];

        private int _clock;

        /// <summary>One case's accepted proposals, new artifacts first, so a fact on an artifact
        /// the same source introduced lands on it.</summary>
        public void Accept(IReadOnlyList<JsonElement> proposals, int caseIndex)
        {
            var names = NamesNow();
            var views = proposals.Select(p => (Raw: p, View: ProposalView.Read(p, names))).ToList();
            foreach (var (_, view) in views.Where(v => v.View.ChangeType == "CreateArtifact"))
            {
                var artifact = Find(view.Subject) ?? Add(view.Subject, view.ArtifactType ?? "Other");
                artifact.Summary = view.Text ?? artifact.Summary;
                artifact.Visibility = view.Visibility ?? artifact.Visibility;
                artifact.Touched = ++_clock;
            }

            names = NamesNow();
            foreach (var (raw, _) in views.Where(v => v.View.ChangeType != "CreateArtifact"))
            {
                Apply(ProposalView.Read(raw, names));
            }
        }

        private void Apply(ProposalView view)
        {
            var artifact = Find(view.Subject);
            switch (view.ChangeType)
            {
                case "UpdateArtifact" when artifact is not null:
                    artifact.Summary = view.Text ?? artifact.Summary;
                    artifact.Visibility = view.Visibility ?? artifact.Visibility;
                    artifact.Touched = ++_clock;
                    break;

                case "AddFact" or "UpdateFact" when artifact is not null && view.Label is { Length: > 0 } predicate:
                    var key = ProposalView.Normalize(predicate);
                    if (!artifact.Facts.TryGetValue(key, out var fact))
                    {
                        fact = artifact.Facts[key] = new FactState(IdFor(world, "fact", $"{artifact.Key}|{key}"), predicate);
                    }

                    fact.Value = view.Text ?? fact.Value;
                    fact.Touched = artifact.Touched = ++_clock;
                    break;

                case "AddRelationship" when artifact is not null && view.Label == "PartOf" && view.Other is { } parent:
                    artifact.PartOf = parent;
                    break;

                case "MergeArtifact" when artifact is not null && view.Other is { } duplicate && Find(duplicate) is { } folded:
                    Artifacts.Remove(folded.Key);
                    Gone[folded.Id] = artifact.Name;
                    foreach (var (factKey, factState) in folded.Facts)
                    {
                        artifact.Facts.TryAdd(factKey, factState);
                    }

                    artifact.Touched = ++_clock;
                    break;
            }
        }

        private ArtifactState? Find(string name) => Artifacts.GetValueOrDefault(ProposalView.Normalize(name));

        private ArtifactState Add(string name, string type)
        {
            var key = ProposalView.Normalize(name);
            return Artifacts[key] = new ArtifactState(IdFor(world, "artifact", key), key, name, type);
        }

        private Dictionary<Guid, string> NamesNow()
        {
            var names = Gone.ToDictionary();
            foreach (var artifact in Artifacts.Values)
            {
                names[artifact.Id] = artifact.Name;
                foreach (var fact in artifact.Facts.Values)
                {
                    names[fact.Id] = $"{artifact.Name} · {fact.Predicate}";
                }
            }

            return names;
        }
    }

    private sealed class ArtifactState(Guid id, string key, string name, string type)
    {
        public Guid Id { get; } = id;
        public string Key { get; } = key;
        public string Name { get; } = name;
        public string Type { get; } = type;
        public string? Summary { get; set; }
        public string? Visibility { get; set; }
        public string? PartOf { get; set; }
        public int Touched { get; set; }
        public Dictionary<string, FactState> Facts { get; } = new(StringComparer.Ordinal);
    }

    private sealed class FactState(Guid id, string predicate)
    {
        public Guid Id { get; } = id;
        public string Predicate { get; } = predicate;
        public string Value { get; set; } = "";
        public int Touched { get; set; }
    }
}
