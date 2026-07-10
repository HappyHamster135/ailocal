using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// Tests that isolate themselves via the process-wide AILOCAL_DATA_DIR
/// environment variable (HostRegistry/WorkerRegistry read it once per
/// instance, not per-call, so it can't be threaded through a parameter)
/// must not run concurrently with each other - xUnit parallelizes across
/// test classes by default, and two classes racing to set/restore the same
/// process environment variable corrupts each other's data directory.
/// </summary>
[CollectionDefinition("EnvIsolated", DisableParallelization = true)]
public class EnvIsolatedCollection
{
}
