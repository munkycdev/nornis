using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nornis.Evals.Corpus;
using Nornis.Evals.Models;
using Nornis.Evals.Running;
using Nornis.Infrastructure.Ai;

namespace Nornis.Evals;

/// <summary>
/// The extraction eval (feature 39). Run from the repository root:
/// <code>
/// dotnet run --project tests/Nornis.Evals -- corpus --spa Z:\repos\dnd\slime-tpk
/// dotnet run --project tests/Nornis.Evals -- run --model openai/gpt-5.4
/// </code>
/// Everything it writes goes under <c>artifacts/evals/</c>, which git ignores.
/// </summary>
public static class Program
{
    private const string Usage = """
        Nornis extraction eval

          corpus [--spa <folder of Slime Spa chapters>] [--out <folder>]
              Writes the corpus: the demo world's notes, and the Slime Spa chapters if named.
          run --model <provider/model> [--corpus <corpus.json>] [--out <folder>] [--repeat <n>]
              Reads every case with one model and saves the run. provider is openai, anthropic,
              gemini, or azure (Nornis's own deployment, e.g. azure/nornis-extract).

        Keys: NORNIS_OPENAI_KEY, NORNIS_ANTHROPIC_KEY, NORNIS_GEMINI_KEY; Azure reads the Worker's
        Extraction settings. Output defaults to artifacts/evals/ under the repository root.
        """;

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            var options = Options(args.Skip(1).ToArray());
            var root = RepositoryRoot();
            var output = options.GetValueOrDefault("out") ?? Path.Combine(root, "artifacts", "evals");
            return args[0] switch
            {
                "corpus" => WriteCorpus(root, options, output),
                "run" => await RunAsync(root, options, output),
                _ => throw new ArgumentException($"Unknown command '{args[0]}'.\n\n{Usage}"),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException or IOException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int WriteCorpus(string root, Dictionary<string, string> options, string output)
    {
        var folder = Path.Combine(output, "corpus");
        Directory.CreateDirectory(folder);
        var worlds = new List<EvalCorpus.ManifestWorld>
        {
            CorpusBuilder.Demo(Path.Combine(root, "src", "Nornis.Api", "DemoTemplate", "vespergale-reach.zip"), folder),
        };
        if (options.GetValueOrDefault("spa") is { } spa)
        {
            worlds.Add(CorpusBuilder.SlimeSpa(spa));
        }

        var manifest = Path.Combine(folder, EvalCorpus.ManifestName);
        EvalCorpus.Save(manifest, new EvalCorpus.Manifest(worlds));
        Console.WriteLine($"{worlds.Sum(w => w.Cases.Count)} cases in {worlds.Count} worlds: {manifest}");
        return 0;
    }

    private static async Task<int> RunAsync(string root, Dictionary<string, string> options, string output)
    {
        var configuration = Configuration(root);
        var model = EvalModel.Parse(Required(options, "model"), configuration);
        var corpus = EvalCorpus.Load(options.GetValueOrDefault("corpus") ?? Path.Combine(output, "corpus", EvalCorpus.ManifestName));
        var repeat = options.TryGetValue("repeat", out var times) ? int.Parse(times) : 1;

        using var userKeys = new UserKeyChatClients(maxRetries: 2);
        using var logging = LoggerFactory.Create(b => b.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
        var runner = new ExtractionRunner(new EvalBinder(model.Bind(configuration, userKeys)), logging,
            configuration.GetValue("Extraction:MaxParseRetryAttempts", 2));

        Console.WriteLine($"{model.Name}: {corpus.Worlds.Sum(w => w.Cases.Count)} cases");
        var run = await runner.RunAsync(corpus, model, EmptyContext.Instance, repeat, CancellationToken.None);

        var folder = Path.Combine(output, "runs");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{run.RanAt:yyyyMMdd-HHmmss}-{model.Name.Replace('/', '_')}.json");
        run.Save(path);
        Console.WriteLine($"Saved {path}");
        return 0;
    }

    /// <summary>The catalog and prices the API and Worker ship with, then the Worker's user
    /// secrets and the environment, as the Worker reads them.</summary>
    private static IConfiguration Configuration(string root) =>
        new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(root, "src", "Nornis.Api", "appsettings.json"))
            .AddJsonFile(Path.Combine(root, "src", "Nornis.Worker", "appsettings.json"))
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Nornis.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Run the eval from inside the Nornis repository (no Nornis.sln above this folder).");
    }

    internal static Dictionary<string, string> Options(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            {
                throw new ArgumentException($"Expected --name value pairs, got '{args[i]}'.");
            }

            options[args[i][2..]] = args[++i];
        }

        return options;
    }

    private static string Required(Dictionary<string, string> options, string name) =>
        options.GetValueOrDefault(name) ?? throw new ArgumentException($"--{name} is required.");
}
