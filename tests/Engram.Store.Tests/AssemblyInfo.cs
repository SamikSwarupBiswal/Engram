using Xunit;

// Disable test parallelization because tests use shared process-wide state (Environment variables)
[assembly: CollectionBehavior(DisableTestParallelization = true)]
