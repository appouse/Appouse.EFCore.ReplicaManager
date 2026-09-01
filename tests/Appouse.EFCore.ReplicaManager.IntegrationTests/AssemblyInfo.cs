using Xunit;

// Each test class boots its own three-node database cluster. Running them in parallel would put a
// dozen database containers on the machine at once, so they are serialised deliberately.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
