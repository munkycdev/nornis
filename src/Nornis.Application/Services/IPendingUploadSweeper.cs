namespace Nornis.Application.Services;

/// <summary>Removes upload rows whose blob never arrived. See <see cref="PendingUploadSweeper"/>.</summary>
public interface IPendingUploadSweeper
{
    Task<PendingUploadSweepResult> SweepAsync(CancellationToken ct);
}
