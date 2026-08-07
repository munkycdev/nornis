using NUnit.Framework;

// Fixtures, for the same reason as Nornis.Api.Tests and with the same isolation argument:
// IntegrationTestBase opens its own SqliteConnection("DataSource=:memory:") per instance,
// and a :memory: database belongs to its connection alone — two fixtures cannot see each
// other's schema or rows. Nothing here mutates environment variables, writes to disk, or
// keeps mutable static state, which is what would make that reasoning fail.
//
// Not ParallelScope.Children: NUnit builds one fixture instance for the whole class, so
// tests inside a fixture share the connection their base class opened. They are isolated
// from other fixtures, not from each other.
[assembly: Parallelizable(ParallelScope.Fixtures)]
