// The localization singleton and the desktop app's service provider are process-wide,
// so test classes run one at a time to keep language/theme state deterministic.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
