namespace Nornis.Application.Prep;

/// <summary>
/// One draft in flight per section (feature 29). In memory, because the API runs as one replica
/// and a lost gate only loses an in-flight draft, never data: the accept is conditional on the
/// section's stamp either way.
/// </summary>
public interface ISectionDraftGate
{
    /// <summary>The section's lease, released by disposing it. Null while another draft holds it.</summary>
    IDisposable? TryEnter(Guid sectionId);
}

/// <inheritdoc cref="ISectionDraftGate"/>
/// <remarks>
/// A lease also expires on its own after <c>leaseLength</c>, longer than any draft can run. That
/// covers a stream the caller never enumerated, whose release would otherwise never run and
/// would hold the section until the API restarted. A holder whose lease expired and was taken
/// over releases nothing when it finally disposes.
/// </remarks>
public sealed class SectionDraftGate : ISectionDraftGate
{
    private readonly Dictionary<Guid, (Guid Token, DateTimeOffset ExpiresAt)> _leases = [];
    private readonly TimeSpan _leaseLength;
    private readonly TimeProvider _time;

    public SectionDraftGate(TimeSpan leaseLength, TimeProvider? time = null)
    {
        _leaseLength = leaseLength;
        _time = time ?? TimeProvider.System;
    }

    public IDisposable? TryEnter(Guid sectionId)
    {
        var now = _time.GetUtcNow();
        lock (_leases)
        {
            if (_leases.TryGetValue(sectionId, out var held) && held.ExpiresAt > now)
            {
                return null;
            }

            var token = Guid.NewGuid();
            _leases[sectionId] = (token, now + _leaseLength);
            return new Lease(this, sectionId, token);
        }
    }

    private void Release(Guid sectionId, Guid token)
    {
        lock (_leases)
        {
            if (_leases.TryGetValue(sectionId, out var held) && held.Token == token)
            {
                _leases.Remove(sectionId);
            }
        }
    }

    private sealed class Lease(SectionDraftGate gate, Guid sectionId, Guid token) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release(sectionId, token);
            }
        }
    }
}
