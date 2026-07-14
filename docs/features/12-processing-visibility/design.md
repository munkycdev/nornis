# Design Document

API: `GET /api/worlds/{worldId}/sources/activity` on SourcesController, composing two
existing visibility-aware reads — `ISourceService.ListByWorldAsync` (counts by
ProcessingStatus) and `IReviewService.ListReviewQueueAsync` (pending count, capped at
its 200 limit with a `capped` flag). No new repository queries; source volumes are
small and both services already enforce role/visibility.

Response: `{ ready, queued, processing, failed, pendingProposals, pendingProposalsCapped }`.

Web: `NavMenu` polls the endpoint every 15s (PeriodicTimer, disposed with the
component) and immediately on world change. Sources nav item shows a `nornis-nav-count`
pill with ready+queued+processing (error-styled with the failed count when failed > 0);
Review shows pending proposals ("200+" when capped).
