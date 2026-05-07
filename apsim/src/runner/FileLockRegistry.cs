using System.Collections.Concurrent;

internal static class FileLockRegistry
{
    internal static readonly ConcurrentDictionary<string, object> Locks = new();
}
