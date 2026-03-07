using Xunit;

// All test classes in the "Sequential" collection run sequentially.
// This is required because method patching modifies shared state (method code)
// and concurrent patching/restoring of the same method would cause races.
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class SequentialCollection { }
