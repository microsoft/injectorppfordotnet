using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace InjectorPP.Net;

/// <summary>
/// Manages per-thread method replacement registrations.
/// Each patched method gets a unique key. Threads register their replacement
/// function pointers independently, enabling thread-safe parallel testing.
/// </summary>
internal static class ThreadLocalRegistry
{
    private static readonly ConcurrentDictionary<int, MethodDispatchEntry> s_entries = new();
    private static int s_methodKeyCounter;

    /// <summary>
    /// Registers a method for thread-local dispatch and returns its unique key.
    /// The original function pointer is stored so the dispatcher can call the original
    /// when no thread-local replacement is registered.
    /// </summary>
    public static int RegisterMethod(IntPtr originalFunctionPointer)
    {
        int key = Interlocked.Increment(ref s_methodKeyCounter);
        s_entries[key] = new MethodDispatchEntry
        {
            OriginalFunctionPointer = originalFunctionPointer,
            ThreadReplacements = new ConcurrentDictionary<int, IntPtr>()
        };
        return key;
    }

    /// <summary>
    /// Adds a thread-local replacement for the current thread.
    /// </summary>
    public static void AddReplacement(int methodKey, IntPtr replacementFunctionPointer)
    {
        if (!s_entries.TryGetValue(methodKey, out var entry))
            throw new InvalidOperationException($"Method key {methodKey} is not registered.");

        int threadId = Environment.CurrentManagedThreadId;
        entry.ThreadReplacements[threadId] = replacementFunctionPointer;
        Interlocked.Increment(ref entry.RefCount);
    }

    /// <summary>
    /// Removes the current thread's replacement.
    /// Returns true if this was the last replacement (method can be unpatched).
    /// </summary>
    public static bool RemoveReplacement(int methodKey)
    {
        if (!s_entries.TryGetValue(methodKey, out var entry))
            return true;

        int threadId = Environment.CurrentManagedThreadId;
        entry.ThreadReplacements.TryRemove(threadId, out _);
        int remaining = Interlocked.Decrement(ref entry.RefCount);
        return remaining <= 0;
    }

    /// <summary>
    /// Gets the function pointer to call for the current thread.
    /// Returns the thread-local replacement if registered, otherwise the original.
    /// Called by generated dispatcher methods on every invocation of a patched method.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IntPtr GetTarget(int methodKey)
    {
        if (!s_entries.TryGetValue(methodKey, out var entry))
            return IntPtr.Zero;

        int threadId = Environment.CurrentManagedThreadId;
        if (entry.ThreadReplacements.TryGetValue(threadId, out var replacement))
            return replacement;

        return entry.OriginalFunctionPointer;
    }

    /// <summary>
    /// Removes a method registration entirely. Called when the method is fully unpatched.
    /// </summary>
    public static void RemoveMethod(int methodKey)
    {
        s_entries.TryRemove(methodKey, out _);
    }
}

internal class MethodDispatchEntry
{
    public IntPtr OriginalFunctionPointer;
    public ConcurrentDictionary<int, IntPtr> ThreadReplacements = new();
    public int RefCount;
}
