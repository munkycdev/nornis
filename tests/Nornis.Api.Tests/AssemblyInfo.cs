using NUnit.Framework;

// Every fixture here builds a full WebApplicationFactory host in [SetUp] — a DI container
// and a fresh in-memory database per test. That is ~200ms a test, and NUnit runs serially
// unless told otherwise, so 476 integration tests cost three minutes of every pipeline and
// each new one adds another fifth of a second forever.
//
// Fixtures, not tests: each fixture owns its own factory and a GUID-named database, so
// fixtures cannot see each other, while tests inside one keep running in declaration order
// against the shared setup they were written against. Test-level scope would be a bet on
// 476 tests none of which was written to make it.
[assembly: Parallelizable(ParallelScope.Fixtures)]
