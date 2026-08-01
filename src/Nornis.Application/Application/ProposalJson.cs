using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nornis.Application.Application;

/// <summary>
/// The one serializer configuration for ProposedValueJson, shared by the validator that
/// gates payloads and the applicator that executes them. A payload accepted by one and
/// unparseable by the other is the failure mode this guards against — the two must
/// never read JSON differently.
///
/// The extractor occasionally quotes a number ("confidence": "0.99"). The extraction
/// boundary normalizes new payloads, but rows stored before that — and hand-edited
/// ones — must still apply, so reading numbers from strings is allowed. Genuinely
/// non-numeric strings still fail.
/// </summary>
public static class ProposalJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
}
