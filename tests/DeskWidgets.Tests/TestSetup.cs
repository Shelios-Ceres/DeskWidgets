// Strings.Language is process-wide and the GDI fallback font is single-threaded:
// run the tests one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
