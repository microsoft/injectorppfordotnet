using System.Collections.Concurrent;
using InjectorPP.Net.Tests.TestTargets;
using Xunit;

namespace InjectorPP.Net.Tests;

/// <summary>
/// Comprehensive thread safety tests for InjectorPP.Net.
/// Verifies that thread-local dispatch correctly isolates
/// method replacements per-thread, enabling parallel test execution.
/// </summary>
public class ThreadSafetyTests
{
    private const int Timeout = 10_000; // 10 seconds

    /// <summary>
    /// Two threads fake the same static method with different return values.
    /// Each thread should see only its own fake.
    /// </summary>
    [Fact]
    public void TwoThreads_SameStaticMethod_DifferentReturnValues()
    {
        int valueFromThread1 = 0;
        int valueFromThread2 = 0;
        Exception? failure = null;

        using var barrier = new Barrier(2);

        var thread1 = new Thread(() =>
        {
            try
            {
                using var injector = new Injector();
                injector.WhenCalled(() => ThreadSafeTarget.GetValue()).WillReturn(100);
                barrier.SignalAndWait(Timeout);
                valueFromThread1 = ThreadSafeTarget.GetValue();
                barrier.SignalAndWait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        var thread2 = new Thread(() =>
        {
            try
            {
                using var injector = new Injector();
                injector.WhenCalled(() => ThreadSafeTarget.GetValue()).WillReturn(200);
                barrier.SignalAndWait(Timeout);
                valueFromThread2 = ThreadSafeTarget.GetValue();
                barrier.SignalAndWait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        thread1.Start();
        thread2.Start();
        thread1.Join(Timeout);
        thread2.Join(Timeout);

        Assert.Null(failure);
        Assert.Equal(100, valueFromThread1);
        Assert.Equal(200, valueFromThread2);
    }

    /// <summary>
    /// A thread without any fake should see the original method behavior,
    /// even when another thread has an active fake for the same method.
    /// </summary>
    [Fact]
    public void ThreadWithoutFake_SeesOriginalBehavior()
    {
        int valueFromFakedThread = 0;
        int valueFromUnfakedThread = 0;
        Exception? failure = null;

        using var barrier = new Barrier(2);

        var fakedThread = new Thread(() =>
        {
            try
            {
                using var injector = new Injector();
                injector.WhenCalled(() => ThreadSafeTarget.GetValue()).WillReturn(999);
                barrier.SignalAndWait(Timeout);
                valueFromFakedThread = ThreadSafeTarget.GetValue();
                barrier.SignalAndWait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        var unfakedThread = new Thread(() =>
        {
            try
            {
                barrier.SignalAndWait(Timeout);
                valueFromUnfakedThread = ThreadSafeTarget.GetValue();
                barrier.SignalAndWait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        fakedThread.Start();
        unfakedThread.Start();
        fakedThread.Join(Timeout);
        unfakedThread.Join(Timeout);

        Assert.Null(failure);
        Assert.Equal(999, valueFromFakedThread);
        Assert.Equal(-1, valueFromUnfakedThread);
    }

    /// <summary>
    /// When one thread disposes its injector, another thread's fake
    /// for the same method should remain active.
    /// </summary>
    [Fact]
    public void DisposeOnOneThread_DoesNotAffectAnother()
    {
        int valueAfterOtherDispose = 0;
        Exception? failure = null;

        using var bothSetUp = new Barrier(2);
        using var thread1Disposed = new ManualResetEventSlim(false);
        using var thread2Verified = new ManualResetEventSlim(false);

        var thread1 = new Thread(() =>
        {
            try
            {
                var injector = new Injector();
                injector.WhenCalled(() => ThreadSafeTarget.GetValue()).WillReturn(111);
                bothSetUp.SignalAndWait(Timeout);
                injector.Dispose();
                thread1Disposed.Set();
                thread2Verified.Wait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        var thread2 = new Thread(() =>
        {
            try
            {
                using var injector = new Injector();
                injector.WhenCalled(() => ThreadSafeTarget.GetValue()).WillReturn(222);
                bothSetUp.SignalAndWait(Timeout);
                thread1Disposed.Wait(Timeout);
                valueAfterOtherDispose = ThreadSafeTarget.GetValue();
                thread2Verified.Set();
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        thread1.Start();
        thread2.Start();
        thread1.Join(Timeout);
        thread2.Join(Timeout);

        Assert.Null(failure);
        Assert.Equal(222, valueAfterOtherDispose);
    }

    /// <summary>
    /// Faking a method, disposing, and faking again on the same thread
    /// should work correctly each time.
    /// </summary>
    [Fact]
    public void SequentialFaking_SameThread_Works()
    {
        // First fake
        using (var injector = new Injector())
        {
            injector.WhenCalled(() => ThreadSafeTarget.GetValue()).WillReturn(10);
            Assert.Equal(10, ThreadSafeTarget.GetValue());
        }

        // Original restored
        Assert.Equal(-1, ThreadSafeTarget.GetValue());

        // Second fake with different value
        using (var injector = new Injector())
        {
            injector.WhenCalled(() => ThreadSafeTarget.GetValue()).WillReturn(20);
            Assert.Equal(20, ThreadSafeTarget.GetValue());
        }

        // Original restored again
        Assert.Equal(-1, ThreadSafeTarget.GetValue());
    }

    /// <summary>
    /// Stress test: 20 threads each fake the same method to their
    /// thread-specific value and verify isolation.
    /// </summary>
    [Fact]
    public void ManyThreads_ConcurrentStressTest()
    {
        const int threadCount = 20;
        var results = new ConcurrentDictionary<int, int>();
        var errors = new ConcurrentBag<Exception>();

        using var barrier = new Barrier(threadCount);

        var threads = new Thread[threadCount];
        for (int i = 0; i < threadCount; i++)
        {
            int expectedValue = (i + 1) * 100;
            threads[i] = new Thread(() =>
            {
                try
                {
                    using var injector = new Injector();
                    injector.WhenCalled(() => ThreadSafeTarget.GetValue()).WillReturn(expectedValue);
                    barrier.SignalAndWait(Timeout);

                    // Read multiple times to stress test
                    for (int j = 0; j < 100; j++)
                    {
                        int actual = ThreadSafeTarget.GetValue();
                        if (actual != expectedValue)
                        {
                            errors.Add(new Exception(
                                $"Expected {expectedValue} but got {actual} on iteration {j}"));
                            return;
                        }
                    }

                    results[expectedValue] = ThreadSafeTarget.GetValue();
                    barrier.SignalAndWait(Timeout);
                }
                catch (Exception ex) { errors.Add(ex); }
            });
        }

        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) thread.Join(Timeout * 3);

        Assert.Empty(errors);
        Assert.Equal(threadCount, results.Count);
        for (int i = 0; i < threadCount; i++)
        {
            int expected = (i + 1) * 100;
            Assert.True(results.ContainsKey(expected),
                $"Missing result for expected value {expected}");
            Assert.Equal(expected, results[expected]);
        }
    }

    /// <summary>
    /// Instance methods should also have thread-local dispatch.
    /// </summary>
    [Fact]
    public void InstanceMethod_ThreadIsolation()
    {
        int valueFromThread1 = 0;
        int valueFromThread2 = 0;
        Exception? failure = null;

        using var barrier = new Barrier(2);

        var thread1 = new Thread(() =>
        {
            try
            {
                var obj = new ThreadSafeInstanceTarget();
                using var injector = new Injector();
                injector.WhenCalled(typeof(ThreadSafeInstanceTarget)
                    .GetMethod(nameof(ThreadSafeInstanceTarget.GetValue))!)
                    .WillReturn(10);
                barrier.SignalAndWait(Timeout);
                valueFromThread1 = obj.GetValue();
                barrier.SignalAndWait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        var thread2 = new Thread(() =>
        {
            try
            {
                var obj = new ThreadSafeInstanceTarget();
                using var injector = new Injector();
                injector.WhenCalled(typeof(ThreadSafeInstanceTarget)
                    .GetMethod(nameof(ThreadSafeInstanceTarget.GetValue))!)
                    .WillReturn(20);
                barrier.SignalAndWait(Timeout);
                valueFromThread2 = obj.GetValue();
                barrier.SignalAndWait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        thread1.Start();
        thread2.Start();
        thread1.Join(Timeout);
        thread2.Join(Timeout);

        Assert.Null(failure);
        Assert.Equal(10, valueFromThread1);
        Assert.Equal(20, valueFromThread2);
    }

    /// <summary>
    /// WillExecute with different delegates per thread.
    /// </summary>
    [Fact]
    public void WillExecute_DifferentDelegatesPerThread()
    {
        int valueFromThread1 = 0;
        int valueFromThread2 = 0;
        Exception? failure = null;

        using var barrier = new Barrier(2);

        var thread1 = new Thread(() =>
        {
            try
            {
                using var injector = new Injector();
                injector.WhenCalled(() => ThreadSafeTarget.GetValue())
                    .WillExecute(() => 42);
                barrier.SignalAndWait(Timeout);
                valueFromThread1 = ThreadSafeTarget.GetValue();
                barrier.SignalAndWait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        var thread2 = new Thread(() =>
        {
            try
            {
                using var injector = new Injector();
                injector.WhenCalled(() => ThreadSafeTarget.GetValue())
                    .WillExecute(() => 84);
                barrier.SignalAndWait(Timeout);
                valueFromThread2 = ThreadSafeTarget.GetValue();
                barrier.SignalAndWait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        thread1.Start();
        thread2.Start();
        thread1.Join(Timeout);
        thread2.Join(Timeout);

        Assert.Null(failure);
        Assert.Equal(42, valueFromThread1);
        Assert.Equal(84, valueFromThread2);
    }

    /// <summary>
    /// WillThrow with different exceptions per thread.
    /// </summary>
    [Fact]
    public void WillThrow_DifferentExceptionsPerThread()
    {
        Exception? exFromThread1 = null;
        Exception? exFromThread2 = null;
        Exception? failure = null;

        using var barrier = new Barrier(2);

        var thread1 = new Thread(() =>
        {
            try
            {
                using var injector = new Injector();
                injector.WhenCalled(() => ThreadSafeTarget.GetValue())
                    .WillThrow<InvalidOperationException>();
                barrier.SignalAndWait(Timeout);
                try { ThreadSafeTarget.GetValue(); }
                catch (Exception ex) { exFromThread1 = ex; }
                barrier.SignalAndWait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        var thread2 = new Thread(() =>
        {
            try
            {
                using var injector = new Injector();
                injector.WhenCalled(() => ThreadSafeTarget.GetValue())
                    .WillThrow<ArgumentException>();
                barrier.SignalAndWait(Timeout);
                try { ThreadSafeTarget.GetValue(); }
                catch (Exception ex) { exFromThread2 = ex; }
                barrier.SignalAndWait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        thread1.Start();
        thread2.Start();
        thread1.Join(Timeout);
        thread2.Join(Timeout);

        Assert.Null(failure);
        Assert.IsType<InvalidOperationException>(exFromThread1);
        Assert.IsType<ArgumentException>(exFromThread2);
    }

    /// <summary>
    /// One thread uses WillDoNothing, another uses WillReturn for the same method.
    /// </summary>
    [Fact]
    public void WillDoNothing_AndWillReturn_PerThread()
    {
        int valueFromDoNothing = int.MinValue;
        int valueFromWillReturn = 0;
        Exception? failure = null;

        using var barrier = new Barrier(2);

        var thread1 = new Thread(() =>
        {
            try
            {
                using var injector = new Injector();
                injector.WhenCalled(() => ThreadSafeTarget.GetValue()).WillDoNothing();
                barrier.SignalAndWait(Timeout);
                valueFromDoNothing = ThreadSafeTarget.GetValue();
                barrier.SignalAndWait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        var thread2 = new Thread(() =>
        {
            try
            {
                using var injector = new Injector();
                injector.WhenCalled(() => ThreadSafeTarget.GetValue()).WillReturn(500);
                barrier.SignalAndWait(Timeout);
                valueFromWillReturn = ThreadSafeTarget.GetValue();
                barrier.SignalAndWait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        thread1.Start();
        thread2.Start();
        thread1.Join(Timeout);
        thread2.Join(Timeout);

        Assert.Null(failure);
        Assert.Equal(0, valueFromDoNothing); // default(int)
        Assert.Equal(500, valueFromWillReturn);
    }

    /// <summary>
    /// Each thread fakes multiple different methods simultaneously.
    /// </summary>
    [Fact]
    public void MultipleMethods_FakedPerThread()
    {
        int val1FromThread1 = 0, val2FromThread1 = 0;
        int val1FromThread2 = 0, val2FromThread2 = 0;
        Exception? failure = null;

        using var barrier = new Barrier(2);

        var thread1 = new Thread(() =>
        {
            try
            {
                using var injector = new Injector();
                injector.WhenCalled(() => ThreadSafeTarget.GetValue()).WillReturn(10);
                injector.WhenCalled(() => ThreadSafeTarget.GetOtherValue()).WillReturn(11);
                barrier.SignalAndWait(Timeout);
                val1FromThread1 = ThreadSafeTarget.GetValue();
                val2FromThread1 = ThreadSafeTarget.GetOtherValue();
                barrier.SignalAndWait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        var thread2 = new Thread(() =>
        {
            try
            {
                using var injector = new Injector();
                injector.WhenCalled(() => ThreadSafeTarget.GetValue()).WillReturn(20);
                injector.WhenCalled(() => ThreadSafeTarget.GetOtherValue()).WillReturn(22);
                barrier.SignalAndWait(Timeout);
                val1FromThread2 = ThreadSafeTarget.GetValue();
                val2FromThread2 = ThreadSafeTarget.GetOtherValue();
                barrier.SignalAndWait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        thread1.Start();
        thread2.Start();
        thread1.Join(Timeout);
        thread2.Join(Timeout);

        Assert.Null(failure);
        Assert.Equal(10, val1FromThread1);
        Assert.Equal(11, val2FromThread1);
        Assert.Equal(20, val1FromThread2);
        Assert.Equal(22, val2FromThread2);
    }

    /// <summary>
    /// Rapid cycles of setup and teardown across multiple threads.
    /// </summary>
    [Fact]
    public void RapidSetupAndTeardown_StressTest()
    {
        const int iterations = 50;
        const int threadCount = 4;
        var errors = new ConcurrentBag<Exception>();

        var threads = new Thread[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            int threadIndex = t;
            threads[t] = new Thread(() =>
            {
                try
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        int expected = (threadIndex * 1000) + i;
                        using var injector = new Injector();
                        injector.WhenCalled(() => ThreadSafeTarget.GetValue()).WillReturn(expected);
                        int actual = ThreadSafeTarget.GetValue();
                        if (actual != expected)
                        {
                            errors.Add(new Exception(
                                $"Thread {threadIndex}, iteration {i}: expected {expected} got {actual}"));
                        }
                    }
                }
                catch (Exception ex) { errors.Add(ex); }
            });
        }

        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) thread.Join(Timeout * 6);

        Assert.Empty(errors);
    }

    /// <summary>
    /// String method return values are isolated per thread.
    /// </summary>
    [Fact]
    public void StringMethod_ThreadIsolation()
    {
        string? nameFromThread1 = null;
        string? nameFromThread2 = null;
        Exception? failure = null;

        using var barrier = new Barrier(2);

        var thread1 = new Thread(() =>
        {
            try
            {
                using var injector = new Injector();
                injector.WhenCalled(() => ThreadSafeTarget.GetName()).WillReturn("Thread1Name");
                barrier.SignalAndWait(Timeout);
                nameFromThread1 = ThreadSafeTarget.GetName();
                barrier.SignalAndWait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        var thread2 = new Thread(() =>
        {
            try
            {
                using var injector = new Injector();
                injector.WhenCalled(() => ThreadSafeTarget.GetName()).WillReturn("Thread2Name");
                barrier.SignalAndWait(Timeout);
                nameFromThread2 = ThreadSafeTarget.GetName();
                barrier.SignalAndWait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        thread1.Start();
        thread2.Start();
        thread1.Join(Timeout);
        thread2.Join(Timeout);

        Assert.Null(failure);
        Assert.Equal("Thread1Name", nameFromThread1);
        Assert.Equal("Thread2Name", nameFromThread2);
    }

    /// <summary>
    /// After all threads dispose, the original method behavior should be fully restored.
    /// </summary>
    [Fact]
    public void AllThreadsDispose_OriginalBehaviorRestored()
    {
        Exception? failure = null;

        using var allSetUp = new Barrier(3);
        using var allVerified = new Barrier(3);

        var threads = new Thread[3];
        for (int i = 0; i < 3; i++)
        {
            int val = (i + 1) * 100;
            threads[i] = new Thread(() =>
            {
                try
                {
                    using var injector = new Injector();
                    injector.WhenCalled(() => ThreadSafeTarget.GetOtherValue()).WillReturn(val);
                    allSetUp.SignalAndWait(Timeout);
                    int actual = ThreadSafeTarget.GetOtherValue();
                    if (actual != val)
                        throw new Exception($"Expected {val} but got {actual}");
                    allVerified.SignalAndWait(Timeout);
                }
                catch (Exception ex) { Volatile.Write(ref failure, ex); }
            });
        }

        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) thread.Join(Timeout);

        Assert.Null(failure);
        // All threads disposed, original should be restored
        Assert.Equal(-2, ThreadSafeTarget.GetOtherValue());
    }

    /// <summary>
    /// Verifies that WillExecute delegates with closures are thread-isolated.
    /// </summary>
    [Fact]
    public void WillExecute_WithClosureCapture_PerThread()
    {
        string? resultFromThread1 = null;
        string? resultFromThread2 = null;
        Exception? failure = null;

        using var barrier = new Barrier(2);

        var thread1 = new Thread(() =>
        {
            try
            {
                string threadLabel = "T1";
                using var injector = new Injector();
                injector.WhenCalled(() => ThreadSafeTarget.GetName())
                    .WillExecute(() => $"Hello from {threadLabel}");
                barrier.SignalAndWait(Timeout);
                resultFromThread1 = ThreadSafeTarget.GetName();
                barrier.SignalAndWait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        var thread2 = new Thread(() =>
        {
            try
            {
                string threadLabel = "T2";
                using var injector = new Injector();
                injector.WhenCalled(() => ThreadSafeTarget.GetName())
                    .WillExecute(() => $"Hello from {threadLabel}");
                barrier.SignalAndWait(Timeout);
                resultFromThread2 = ThreadSafeTarget.GetName();
                barrier.SignalAndWait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        thread1.Start();
        thread2.Start();
        thread1.Join(Timeout);
        thread2.Join(Timeout);

        Assert.Null(failure);
        Assert.Equal("Hello from T1", resultFromThread1);
        Assert.Equal("Hello from T2", resultFromThread2);
    }

    /// <summary>
    /// Simulates parallel test methods — multiple independent "tests" running
    /// on different threads, each faking multiple methods with different values.
    /// </summary>
    [Fact]
    public void SimulatedParallelTests_NoInterference()
    {
        var errors = new ConcurrentBag<Exception>();
        using var allReady = new Barrier(4);

        var testConfigs = new[]
        {
            (Name: "Test1", IntVal: 100, StrVal: "Name1"),
            (Name: "Test2", IntVal: 200, StrVal: "Name2"),
            (Name: "Test3", IntVal: 300, StrVal: "Name3"),
            (Name: "Test4", IntVal: 400, StrVal: "Name4"),
        };

        var threads = testConfigs.Select(cfg => new Thread(() =>
        {
            try
            {
                using var injector = new Injector();
                injector.WhenCalled(() => ThreadSafeTarget.GetValue()).WillReturn(cfg.IntVal);
                injector.WhenCalled(() => ThreadSafeTarget.GetName()).WillReturn(cfg.StrVal);
                allReady.SignalAndWait(Timeout);

                // Each "test" verifies its own faked values repeatedly
                for (int i = 0; i < 50; i++)
                {
                    int actualInt = ThreadSafeTarget.GetValue();
                    string actualStr = ThreadSafeTarget.GetName();
                    if (actualInt != cfg.IntVal || actualStr != cfg.StrVal)
                    {
                        errors.Add(new Exception(
                            $"{cfg.Name}: expected ({cfg.IntVal}, {cfg.StrVal}) " +
                            $"but got ({actualInt}, {actualStr})"));
                        return;
                    }
                }
            }
            catch (Exception ex) { errors.Add(ex); }
        })).ToArray();

        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) thread.Join(Timeout);

        Assert.Empty(errors);
    }

    /// <summary>
    /// Bool method: one thread fakes to true, another to false (opposite of default).
    /// </summary>
    [Fact]
    public void BoolMethod_ThreadIsolation()
    {
        bool valueFromThread1 = false;
        bool valueFromThread2 = true;
        Exception? failure = null;

        using var barrier = new Barrier(2);

        var thread1 = new Thread(() =>
        {
            try
            {
                using var injector = new Injector();
                injector.WhenCalled(() => ThreadSafeTarget.IsEnabled()).WillReturn(true);
                barrier.SignalAndWait(Timeout);
                valueFromThread1 = ThreadSafeTarget.IsEnabled();
                barrier.SignalAndWait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        var thread2 = new Thread(() =>
        {
            try
            {
                // Thread 2 does NOT fake — should see original (false)
                barrier.SignalAndWait(Timeout);
                valueFromThread2 = ThreadSafeTarget.IsEnabled();
                barrier.SignalAndWait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        thread1.Start();
        thread2.Start();
        thread1.Join(Timeout);
        thread2.Join(Timeout);

        Assert.Null(failure);
        Assert.True(valueFromThread1);
        Assert.False(valueFromThread2);
    }

    /// <summary>
    /// Void method: one thread uses WillDoNothing, another uses WillExecute,
    /// a third sees original behavior.
    /// </summary>
    [Fact]
    public void VoidMethod_ThreeThreads_DifferentBehaviors()
    {
        bool thread1Executed = false;
        bool thread2DelegateRan = false;
        Exception? failure = null;

        using var barrier = new Barrier(3);

        var thread1 = new Thread(() =>
        {
            try
            {
                using var injector = new Injector();
                injector.WhenCalled(() => ThreadSafeTarget.DoSomething()).WillDoNothing();
                barrier.SignalAndWait(Timeout);
                ThreadSafeTarget.DoSomething();
                thread1Executed = true;
                barrier.SignalAndWait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        var thread2 = new Thread(() =>
        {
            try
            {
                using var injector = new Injector();
                injector.WhenCalled(() => ThreadSafeTarget.DoSomething())
                    .WillExecute(() => { thread2DelegateRan = true; });
                barrier.SignalAndWait(Timeout);
                ThreadSafeTarget.DoSomething();
                barrier.SignalAndWait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        var thread3 = new Thread(() =>
        {
            try
            {
                // No fake — sees original behavior
                barrier.SignalAndWait(Timeout);
                ThreadSafeTarget.DoSomething();
                barrier.SignalAndWait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        thread1.Start();
        thread2.Start();
        thread3.Start();
        thread1.Join(Timeout);
        thread2.Join(Timeout);
        thread3.Join(Timeout);

        Assert.Null(failure);
        Assert.True(thread1Executed);
        Assert.True(thread2DelegateRan);
    }

    /// <summary>
    /// Instance method: two threads fake GetName on different instances,
    /// verifying thread isolation works per-method not per-instance.
    /// </summary>
    [Fact]
    public void InstanceMethod_StringReturn_ThreadIsolation()
    {
        string? nameFromThread1 = null;
        string? nameFromThread2 = null;
        Exception? failure = null;

        using var barrier = new Barrier(2);

        var thread1 = new Thread(() =>
        {
            try
            {
                var obj = new ThreadSafeInstanceTarget();
                using var injector = new Injector();
                injector.WhenCalled(typeof(ThreadSafeInstanceTarget)
                    .GetMethod(nameof(ThreadSafeInstanceTarget.GetName))!)
                    .WillReturn("FromThread1");
                barrier.SignalAndWait(Timeout);
                nameFromThread1 = obj.GetName();
                barrier.SignalAndWait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        var thread2 = new Thread(() =>
        {
            try
            {
                var obj = new ThreadSafeInstanceTarget();
                using var injector = new Injector();
                injector.WhenCalled(typeof(ThreadSafeInstanceTarget)
                    .GetMethod(nameof(ThreadSafeInstanceTarget.GetName))!)
                    .WillReturn("FromThread2");
                barrier.SignalAndWait(Timeout);
                nameFromThread2 = obj.GetName();
                barrier.SignalAndWait(Timeout);
            }
            catch (Exception ex) { Volatile.Write(ref failure, ex); }
        });

        thread1.Start();
        thread2.Start();
        thread1.Join(Timeout);
        thread2.Join(Timeout);

        Assert.Null(failure);
        Assert.Equal("FromThread1", nameFromThread1);
        Assert.Equal("FromThread2", nameFromThread2);
    }
}
