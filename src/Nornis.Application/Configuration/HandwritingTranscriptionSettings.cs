namespace Nornis.Application.Configuration;

/// <summary>
/// What a host's handwriting transcription calls should say about themselves. Supplied per
/// host rather than read from one configuration section, because the two hosts that
/// transcribe reach different Azure OpenAI deployments: the worker calls the extraction
/// deployment, the API calls the one its user-facing clients share.
/// </summary>
/// <param name="Model">
/// The deployment name this host's <c>ChatClient</c> actually targets — a label, not a
/// route; the client is already bound to a deployment. It becomes the usage record's model,
/// which is the key <c>AiUsageRecorder</c> prices against, so a name with no
/// <c>ModelPricing</c> entry in the host's configuration records the call at $0 and leaves
/// the daily budget guard reading a spend that never rises.
/// </param>
/// <param name="TimeoutSeconds">
/// How long to wait for the vision call. Longer than a text completion deserves: several
/// photographed pages are a large prompt, and the caller is a person who would rather wait
/// than be told to retake a photo that was fine.
/// </param>
public record HandwritingTranscriptionSettings(string Model, int TimeoutSeconds);
