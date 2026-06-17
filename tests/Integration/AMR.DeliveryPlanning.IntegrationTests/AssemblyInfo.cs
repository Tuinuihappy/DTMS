using Xunit;

// Every test class in this project owns a Testcontainers PostgreSQL container
// via IClassFixture<DtmsWebApplicationFactory>. Running classes in parallel
// would spin up N containers simultaneously and exhaust Docker resources or
// hit per-engine throttles, surfacing as the "container start timeout"
// failures we see when the suite runs full vs. filtered. Force sequential
// execution at the assembly level so each class gets its container in turn.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
