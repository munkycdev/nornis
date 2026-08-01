using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Services;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

/// <summary>
/// Wraps the in-memory usage repository in a real <see cref="AiUsageRecorder"/> so
/// service tests keep asserting on the rows the repository captured. Pass the options
/// object a test configures pricing on; unpassed sections price everything at zero.
/// </summary>
public static class TestUsageRecorder
{
    public static AiUsageRecorder Wrap(
        IAiUsageRecordRepository repository,
        ExtractionOptions? extraction = null,
        LoremasterOptions? loremaster = null,
        LibraryOptions? library = null,
        IAiOutcomeMonitor? outcomeMonitor = null) =>
        new(repository,
            outcomeMonitor ?? new AiOutcomeMonitor(),
            Options.Create(extraction ?? new ExtractionOptions()),
            Options.Create(loremaster ?? new LoremasterOptions()),
            Options.Create(library ?? new LibraryOptions()));
}
