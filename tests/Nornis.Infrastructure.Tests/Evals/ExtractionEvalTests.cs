using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Ai;
using Nornis.Domain.Enums;
using Nornis.Evals.Corpus;
using Nornis.Evals.Models;
using Nornis.Evals.Running;
using Nornis.Evals.Scoring;
using Nornis.Infrastructure.Ai;
using Nornis.Infrastructure.Tests.Ai;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Evals;

/// <summary>
/// The extraction eval (feature 39, C1) without a model: the corpus it builds, the world it
/// replays, how it scores, and the baseline checklist's round trip. The eval itself only ever
/// runs by hand.
/// </summary>
[TestFixture]
public class ExtractionEvalTests
{
    private static readonly EvalWorld World = new("w", "World",
    [
        Case("one", "Session 1", "The harbormaster Quell met the party at Saltmere."),
        Case("two", "Session 2", "Quell lied about the wreck. The Salt Factor paid him."),
        Case("three", "Session 3", "Nobody of note.", visibility: "PartyVisible"),
    ]);

    private static readonly EvalCorpus Corpus = new([World]);

    [Test]
    public void The_demo_corpus_reads_the_gms_notes_first_then_the_sessions_as_played()
    {
        var folder = Folder();
        var world = CorpusBuilder.Demo(TemplateZip(), folder);
        EvalCorpus.Save(Path.Combine(folder, EvalCorpus.ManifestName), new EvalCorpus.Manifest([world]));

        var loaded = EvalCorpus.Load(Path.Combine(folder, EvalCorpus.ManifestName)).Worlds.Single();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Cases.Select(c => c.Type), Is.EqualTo(new[] { "GMNote", "GMNote", "GMNote" }.Concat(Enumerable.Repeat("SessionNote", 6))));
            Assert.That(loaded.Cases[3].Title, Does.StartWith("Session 1"));
            Assert.That(loaded.Cases[^1].Id, Is.EqualTo("session-6"));
            Assert.That(loaded.Cases.Where(c => c.Type == "SessionNote").Select(c => c.Campaign), Has.All.EqualTo("The Vesper Bell"));
            Assert.That(loaded.Cases.Select(c => c.Title), Has.None.StartsWith("Artifact merge"));
            Assert.That(loaded.Cases, Has.All.Matches<EvalCase>(c => c is { Body.Length: > 100 }));
        });
    }

    [Test]
    public void A_missing_corpus_file_is_an_error_not_a_smaller_corpus()
    {
        var folder = Folder();
        EvalCorpus.Save(Path.Combine(folder, EvalCorpus.ManifestName), new EvalCorpus.Manifest(
            [new EvalCorpus.ManifestWorld("w", "W", [new EvalCorpus.ManifestCase("a", "A", "SessionNote", "PartyVisible", "missing.md")])]));

        Assert.Throws<FileNotFoundException>(() => EvalCorpus.Load(Path.Combine(folder, EvalCorpus.ManifestName)));
    }

    [Test]
    public void A_later_source_is_read_against_what_the_earlier_ones_left()
    {
        var replay = WorldReplay.From(Kept(
            [Create("Harbormaster Quell", "Character", "Saltmere's harbormaster."), FactByName("Harbormaster Quell", "role", "harbormaster of Saltmere")],
            [],
            []));

        var before = replay.Before(World, 1);
        var quell = before.Single();
        Assert.Multiple(() =>
        {
            Assert.That(replay.Before(World, 0), Is.Empty);
            Assert.That(quell.Name, Is.EqualTo("Harbormaster Quell"));
            Assert.That(quell.Summary, Is.EqualTo("Saltmere's harbormaster."));
            Assert.That(quell.Facts.Single().Value, Is.EqualTo("harbormaster of Saltmere"));
            Assert.That(replay.Names("w")[quell.Id], Is.EqualTo("Harbormaster Quell"), "ids are named, so a reply that uses one can be read");
            Assert.That(WorldReplay.From(Kept([Create("Harbormaster Quell", "Character", "x")], [], [])).Before(World, 1).Single().Id,
                Is.EqualTo(quell.Id), "the same name always gets the same id");
        });
    }

    [Test]
    public void A_party_visible_source_never_sees_gm_only_artifacts()
    {
        var replay = WorldReplay.From(Kept(
            [Create("Harbormaster Quell", "Character", "Open.", "PartyVisible"), Create("The Castellan", "Character", "Secret.", "GMOnly")],
            [],
            []));

        Assert.That(replay.Before(World, 2).Select(a => a.Name), Is.EqualTo(["Harbormaster Quell"]));
    }

    [Test]
    public void An_update_by_id_changes_the_fact_it_names()
    {
        var first = WorldReplay.From(Kept([Create("Quell", "Character", "x"), FactByName("Quell", "loyalty", "the harbor")], [], []));
        var factId = first.Before(World, 1).Single().Facts.Single().Id;

        var replay = WorldReplay.From(Kept(
            [Create("Quell", "Character", "x"), FactByName("Quell", "loyalty", "the harbor")],
            [Proposal("UpdateFact", factId, new { value = "the Salt Factor" })],
            []));

        Assert.That(replay.Before(World, 2).Single().Facts.Single().Value, Is.EqualTo("the Salt Factor"));
    }

    [Test]
    public void Proposals_meet_on_kind_and_name_whatever_their_case_or_articles()
    {
        var names = new Dictionary<Guid, string>();
        var baseline = new[]
        {
            Create("The Salt Factor", "Faction", "A smuggling house."),
            FactByName("Harbormaster Quell", "loyalty", "secretly paid by the Salt Factor"),
            Link("Quell", "Salt Factor", "PaidBy"),
            Create("Widow Harrach", "Character", "A tide-reader."),
        }.Select(p => ProposalView.Read(p, names)).ToList();
        var answer = new[]
        {
            Create("salt factor", "Faction", "Smugglers."),
            FactByName("Quell", "allegiance", "paid by the Salt Factor in secret"),
            Link("The Salt Factor", "Quell", "Pays"),
            Create("Bleakspire Keep", "Location", "A fortress."),
        }.Select(p => ProposalView.Read(p, names)).ToList();

        var score = Scorer.Score(baseline, answer);

        Assert.Multiple(() =>
        {
            Assert.That(score.Found, Has.Count.EqualTo(3));
            Assert.That(score.Missed.Single().Subject, Is.EqualTo("Widow Harrach"));
            Assert.That(score.Invented.Single().Subject, Is.EqualTo("Bleakspire Keep"));
        });
    }

    [Test]
    public void A_fact_that_says_something_else_about_the_same_artifact_is_not_a_match()
    {
        var names = new Dictionary<Guid, string>();
        var score = Scorer.Score(
            [ProposalView.Read(FactByName("Quell", "loyalty", "the Salt Factor"), names)],
            [ProposalView.Read(FactByName("Quell", "height", "very tall"), names)]);

        Assert.That((score.Found.Count, score.Missed.Count, score.Invented.Count), Is.EqualTo((0, 1, 1)));
    }

    [Test]
    public void Each_baseline_proposal_is_matched_once()
    {
        var names = new Dictionary<Guid, string>();
        var quell = ProposalView.Read(Create("Quell", "Character", "x"), names);

        var score = Scorer.Score([quell], [quell, quell]);

        Assert.That((score.Found.Count, score.Invented.Count), Is.EqualTo((1, 1)));
    }

    [Test]
    public void The_baseline_checklist_round_trips_and_an_unticked_line_is_dropped()
    {
        var folder = Folder();
        var run = new RunRecord("openai/gpt-5.4", DateTimeOffset.UnixEpoch, 1,
        [
            Record("one", [Create("Quell", "Character", "Harbormaster."), Create("Bleakspire Keep", "Location", "A keep.")]),
            Record("two", null, "Proposal[0] has empty Rationale."),
            Record("three", []),
        ]);

        Baseline.Write(folder, Corpus, run);
        var checklist = Path.Combine(folder, "w.md");
        var text = File.ReadAllText(checklist);
        File.WriteAllText(checklist, text.Replace("- [x] `one#2`", "- [ ] `one#2`"));
        var kept = Baseline.Load(folder, Corpus)["w"];

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("- [x] `one#1` **New** Quell (Character, PartyVisible): Harbormaster."));
            Assert.That(text, Does.Contain("No valid answer: Proposal[0] has empty Rationale."));
            Assert.That(text, Does.Contain("_Nothing proposed._"));
            Assert.That(kept.Select(c => c.Count), Is.EqualTo([1, 0, 0]));
            Assert.That(kept[0][0].GetProperty("proposedValue").GetProperty("name").GetString(), Is.EqualTo("Quell"));
        });
    }

    [Test]
    public void Ticks_are_read_from_list_lines_only()
    {
        var ticks = Baseline.ReadTicks("""
            Some text with `one#9` in it.
            - [x] `one#1` **New** Quell
            - [ ] `one#2` **New** Keep
            * [X] `two#1` **Fact** Quell · loyalty
            """).ToList();

        Assert.That(ticks, Is.EqualTo([("one#1", true), ("one#2", false), ("two#1", true)]));
    }

    [Test]
    public async Task A_run_reads_each_source_against_the_replayed_world_and_retries_a_refused_answer()
    {
        var chat = new FakeChatClient()
            .Returns(Answer(Create("Quell", "Character", "Harbormaster.")))
            .Returns("{ not json")
            .Returns(Answer(FactByName("Quell", "loyalty", "the Salt Factor")))
            .Returns(Answer());
        var binding = new AiBinding(chat, OpenAiProviderTraits.Instance, new AiFunding(AiProvider.OpenAi, "gpt-5.4", AiFundedBy.Key));
        var runner = new ExtractionRunner(new EvalBinder(binding), NullLoggerFactory.Instance);
        var model = new EvalModel("openai/gpt-5.4", AiProvider.OpenAi, "gpt-5.4", null);

        var run = await runner.RunAsync(Corpus, model, WorldReplay.AcceptAsRead(), repeat: 1, CancellationToken.None);

        var second = chat.Calls[2].Messages.Single(m => m.Role == ChatRole.User).Text;
        Assert.Multiple(() =>
        {
            Assert.That(run.Cases.Select(c => c.Proposals?.Count), Is.EqualTo(new int?[] { 1, 1, 0 }));
            Assert.That(run.Cases[1].Failures, Has.Count.EqualTo(1), "the unparseable answer was refused and asked again");
            Assert.That(run.Cases[1].Usage, Has.Count.EqualTo(2), "both attempts were paid for");
            Assert.That(run.Cases[1].KnownArtifacts, Is.EqualTo(1));
            Assert.That(second, Does.Contain("## Existing World Artifacts").And.Contain("### Quell"));
        });
    }

    [Test]
    public void The_report_counts_what_was_found_missed_and_invented()
    {
        var kept = Kept([Create("Quell", "Character", "Harbormaster."), Create("Widow Harrach", "Character", "Tide-reader.")], [], []);
        var run = new RunRecord("openai/gpt-5.4", DateTimeOffset.UnixEpoch, 1,
        [
            Record("one", [Create("Quell", "Character", "The harbormaster."), Create("Bleakspire Keep", "Location", "A keep.")]),
            Record("two", []),
            Record("three", null, "HTTP 529: overloaded"),
        ]);

        var report = Report.Write(Corpus, run, kept, new Nornis.Application.Configuration.AiModelPrice(1m, null, 10m));

        Assert.Multiple(() =>
        {
            Assert.That(report, Does.Contain("| Found | 1 of 2 (50%) |"));
            Assert.That(report, Does.Contain("| Invented (not in the baseline) | 1 (50% of what it proposed) |"));
            Assert.That(report, Does.Contain("| Sources with no valid answer | 1 of 3 |"));
            Assert.That(report, Does.Contain("Missed:").And.Contain("**New** Widow Harrach"));
            Assert.That(report, Does.Contain("Invented:").And.Contain("**New** Bleakspire Keep"));
        });
    }

    [Test]
    public async Task The_judge_rules_only_on_what_the_matching_left_and_never_learns_the_model()
    {
        var names = new Dictionary<Guid, string>();
        var reference = new[] { Create("Widow Harrach", "Character", "A tide-reader."), Create("Quell", "Character", "Harbormaster.") }
            .Select(p => ProposalView.Read(p, names)).ToList();
        var answer = new[] { Create("Quell", "Character", "Harbormaster."), Create("Mother Harrach", "Character", "Reads tides."), Create("Bleakspire Keep", "Location", "A keep.") }
            .Select(p => ProposalView.Read(p, names)).ToList();
        var score = Scorer.Score(reference, answer);
        var chat = new FakeChatClient().Returns("""
            {"recovered":[{"reference":"R1","candidate":"C1"},{"reference":"R2","candidate":"C2"},{"reference":"R9","candidate":"C2"}],
             "candidates":[{"candidate":"C1","verdict":"supported","reason":"used twice"},{"candidate":"C2","verdict":"unsupported","reason":"No keep is mentioned."}],
             "pairs":[]}
            """);
        var judge = new Judge(new EvalBinder(new AiBinding(chat, OpenAiProviderTraits.Instance, new AiFunding(AiProvider.Anthropic, "judge", AiFundedBy.Key))),
            "judge", NullLogger.Instance);

        var judged = await judge.JudgeAsync("w", World.Cases[0], 1, score, reference, CancellationToken.None);

        var sent = chat.Calls.Single().Messages.Single(m => m.Role == ChatRole.User).Text;
        Assert.Multiple(() =>
        {
            Assert.That(score.Missed.Single().Subject, Is.EqualTo("Widow Harrach"), "the key matching can't see the widow");
            Assert.That(sent, Does.Contain(World.Cases[0].Body).And.Contain("R1. **New** Widow Harrach").And.Contain("C1. **New** Mother Harrach"));
            Assert.That(sent, Does.Not.Contain("gpt").And.Not.Contain("openai"), "the judge isn't told whose proposals these are");
            Assert.That(judged.Recovered.Single(), Is.EqualTo(new JudgedMatch(reference[0].Describe(), answer[1].Describe())),
                "R2 was already matched and R9 doesn't exist, so neither counts");
            Assert.That(judged.Candidates.Single(), Is.EqualTo(new JudgedCandidate(answer[2].Describe(), Judge.Unsupported, "No keep is mentioned.")),
                "a candidate used as a match gets no second verdict");
            Assert.That(judged.Usage, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void A_judge_that_fails_leaves_the_case_unjudged_rather_than_failing_the_run()
    {
        var names = new Dictionary<Guid, string>();
        var score = Scorer.Score([ProposalView.Read(Create("Quell", "Character", "x"), names)], []);
        var chat = new FakeChatClient().Returns("not json");
        var judge = new Judge(new EvalBinder(new AiBinding(chat, OpenAiProviderTraits.Instance, new AiFunding(AiProvider.OpenAi, "judge", AiFundedBy.Key))),
            "judge", NullLogger.Instance);

        var judged = judge.JudgeAsync("w", World.Cases[0], 1, score, [], CancellationToken.None).Result;

        Assert.That(judged.Failure, Is.Not.Null);
    }

    [Test]
    public void The_report_counts_what_the_judge_matched_and_splits_what_was_invented()
    {
        var kept = Kept([Create("Widow Harrach", "Character", "Tide-reader."), Create("Quell", "Character", "Harbormaster.")], [], []);
        var run = new RunRecord("openai/gpt-5.4", DateTimeOffset.UnixEpoch, 1,
        [
            Record("one", [Create("Quell", "Character", "Harbormaster."), Create("Mother Harrach", "Character", "Reads tides."), Create("Bleakspire Keep", "Location", "A keep.")]),
            Record("two", []),
            Record("three", []),
        ]);
        var names = new Dictionary<Guid, string>();
        string Line(JsonElement p) => ProposalView.Read(p, names).Describe();
        var judged = new JudgeRecord("anthropic/claude-opus-5-5", DateTimeOffset.UnixEpoch,
        [
            new CaseJudgement("w", "one", 1,
                [new JudgedMatch(Line(Create("Widow Harrach", "Character", "Tide-reader.")), Line(Create("Mother Harrach", "Character", "Reads tides.")))],
                [new JudgedCandidate(Line(Create("Bleakspire Keep", "Location", "A keep.")), Judge.Unsupported, "No keep is mentioned.")],
                [],
                [new AiUsage { Model = "j", InputTokens = 1000, OutputTokens = 100, TotalTokens = 1100, DurationMs = 1 }],
                null),
        ]);

        var report = Report.Write(Corpus, run, kept, null, judged, new Nornis.Application.Configuration.AiModelPrice(5m, null, 25m));

        Assert.Multiple(() =>
        {
            Assert.That(report, Does.Contain("| Found | 1 of 2 (50%) |"), "the key matching's score stands as it is");
            Assert.That(report, Does.Contain("| Found, counting what the judge matched | 2 of 2 (100%) |"));
            Assert.That(report, Does.Contain("| Invented that the source doesn't support | 1 |"));
            Assert.That(report, Does.Contain("| Judge's cost | $0.008 |"));
            Assert.That(report, Does.Contain("_(judge: unsupported; No keep is mentioned.)_"));
            Assert.That(report, Does.Contain("_(judge: covered by **New** Mother Harrach"));
        });
    }

    [Test]
    public void A_model_name_reads_as_provider_and_model_and_is_priced_from_the_catalog()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiModels:Catalog:0:Id"] = "gemini/gemini-3.8-flash",
                ["AiModels:Catalog:0:Provider"] = "Gemini",
                ["AiModels:Catalog:0:Model"] = "gemini-3.8-flash",
                ["AiModels:Catalog:0:InputPerMillionUsd"] = "1.5",
                ["AiModels:Catalog:0:OutputPerMillionUsd"] = "7.5",
                ["Extraction:ModelPricing:nornis-extract:InputPerMillionTokensUsd"] = "2.5",
                ["Extraction:ModelPricing:nornis-extract:OutputPerMillionTokensUsd"] = "15",
            })
            .Build();

        var gemini = EvalModel.Parse("Gemini/gemini-3.8-flash", configuration);
        var azure = EvalModel.Parse("azure/nornis-extract", configuration);

        Assert.Multiple(() =>
        {
            Assert.That((gemini.Provider, gemini.Model, gemini.Price?.OutputPerMillionUsd), Is.EqualTo((AiProvider.Gemini, "gemini-3.8-flash", 7.5m)));
            Assert.That((azure.Provider, azure.Price?.InputPerMillionUsd), Is.EqualTo((AiProvider.AzureOpenAi, 2.5m)));
            Assert.That(EvalModel.Parse("openai/unlisted", configuration).Price, Is.Null);
            Assert.Throws<ArgumentException>(() => EvalModel.Parse("mistral/large", configuration));
        });
    }

    private static EvalCase Case(string id, string title, string body, string visibility = "GMOnly") =>
        new(id, title, "SessionNote", visibility, null, null, body);

    private static Dictionary<string, IReadOnlyList<IReadOnlyList<JsonElement>>> Kept(params JsonElement[][] cases) =>
        new() { ["w"] = cases.Select(c => (IReadOnlyList<JsonElement>)c).ToList() };

    private static CaseRecord Record(string id, JsonElement[]? proposals, params string[] failures) =>
        new("w", id, 1, 0, proposals, failures, [new AiUsage { Model = "m", InputTokens = 1000, OutputTokens = 100, TotalTokens = 1100, DurationMs = 1000 }]);

    private static JsonElement Create(string name, string type, string summary, string visibility = "PartyVisible") =>
        Proposal("CreateArtifact", null, new { name, type, summary, visibility });

    private static JsonElement FactByName(string artifactName, string predicate, string value) =>
        Proposal("AddFact", null, new { artifactName, predicate, value });

    private static JsonElement Link(string a, string b, string type) =>
        Proposal("AddRelationship", null, new { artifactAName = a, artifactBName = b, type });

    private static JsonElement Proposal(string changeType, Guid? targetId, object value) =>
        JsonSerializer.SerializeToElement(new
        {
            changeType,
            targetType = changeType.Contains("Fact") ? "ArtifactFact" : changeType.Contains("Relationship") ? "ArtifactRelationship" : "Artifact",
            targetId,
            proposedValue = value,
            rationale = "Because the source says so.",
            confidence = 0.9,
        });

    /// <summary>A strict-schema answer: every payload field present, null where unused.</summary>
    private static string Answer(params JsonElement[] proposals)
    {
        string[] fields = ["name", "type", "summary", "visibility", "confidence", "status", "sourceArtifactId", "predicate", "value", "truthState",
            "artifactName", "artifactAId", "artifactBId", "artifactAName", "artifactBName", "description"];
        var full = proposals.Select(p =>
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(p.GetRawText())!.AsObject();
            var value = node["proposedValue"]!.AsObject();
            foreach (var field in fields.Where(f => !value.ContainsKey(f)))
            {
                value[field] = null;
            }

            node["quote"] = null;
            return node;
        });
        return new System.Text.Json.Nodes.JsonObject { ["proposals"] = new System.Text.Json.Nodes.JsonArray(full.ToArray()) }.ToJsonString();
    }

    private static string TemplateZip()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Nornis.sln")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "src", "Nornis.Api", "DemoTemplate", "vespergale-reach.zip");
    }

    private static string Folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "nornis-evals-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
