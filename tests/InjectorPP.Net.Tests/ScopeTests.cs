using InjectorPP.Net.Tests.TestTargets;
using Xunit;

namespace InjectorPP.Net.Tests;

/// <summary>
/// Tests for injection scoping - verifying that original method behavior is restored
/// after the injector is disposed. Mirrors the C++ "Limit the scope" pattern.
/// </summary>
public class ScopeTests
{
    [Fact]
    public void AfterDispose_OriginalBehaviorIsRestored()
    {
        // Verify original behavior
        Assert.False(FooClass.StaticMemberFunctionReturnFalse());

        // Create scoped injection
        {
            var injector = new Injector();
            injector.WhenCalled(typeof(FooClass).GetMethod(nameof(FooClass.StaticMemberFunctionReturnFalse))!)
                    .WillReturn(true);

            Assert.True(FooClass.StaticMemberFunctionReturnFalse());

            injector.Dispose();
        }

        // Original behavior should be restored
        Assert.False(FooClass.StaticMemberFunctionReturnFalse());
    }

    [Fact]
    public void UsingStatement_AutomaticallyRestoresOriginalBehavior()
    {
        Assert.Equal(42, FooClass.StaticMemberFunctionReturnInt());

        using (var injector = new Injector())
        {
            injector.WhenCalled(typeof(FooClass).GetMethod(nameof(FooClass.StaticMemberFunctionReturnInt))!)
                    .WillReturn(999);

            Assert.Equal(999, FooClass.StaticMemberFunctionReturnInt());
        }

        Assert.Equal(42, FooClass.StaticMemberFunctionReturnInt());
    }

    [Fact]
    public void MultipleReplacements_AllRestoredOnDispose()
    {
        Assert.False(FooClass.StaticMemberFunctionReturnFalse());
        Assert.Equal(42, FooClass.StaticMemberFunctionReturnInt());
        Assert.Equal("OriginalValue", FooClass.StaticMemberFunctionReturnString());

        using (var injector = new Injector())
        {
            injector.WhenCalled(typeof(FooClass).GetMethod(nameof(FooClass.StaticMemberFunctionReturnFalse))!)
                    .WillReturn(true);
            injector.WhenCalled(typeof(FooClass).GetMethod(nameof(FooClass.StaticMemberFunctionReturnInt))!)
                    .WillReturn(999);
            injector.WhenCalled(typeof(FooClass).GetMethod(nameof(FooClass.StaticMemberFunctionReturnString))!)
                    .WillReturn("Faked");

            Assert.True(FooClass.StaticMemberFunctionReturnFalse());
            Assert.Equal(999, FooClass.StaticMemberFunctionReturnInt());
            Assert.Equal("Faked", FooClass.StaticMemberFunctionReturnString());
        }

        Assert.False(FooClass.StaticMemberFunctionReturnFalse());
        Assert.Equal(42, FooClass.StaticMemberFunctionReturnInt());
        Assert.Equal("OriginalValue", FooClass.StaticMemberFunctionReturnString());
    }

    [Fact]
    public void InstanceMethod_RestoredAfterDispose()
    {
        var foo = new FooClass();
        Assert.False(foo.MemberFunctionReturnFalse());

        using (var injector = new Injector())
        {
            injector.WhenCalled(
                    typeof(FooClass).GetMethod(nameof(FooClass.MemberFunctionReturnFalse), Type.EmptyTypes)!)
                    .WillReturn(true);

            Assert.True(foo.MemberFunctionReturnFalse());
        }

        Assert.False(foo.MemberFunctionReturnFalse());
    }

    [Fact]
    public void DisposeCalledMultipleTimes_ShouldNotThrow()
    {
        var injector = new Injector();
        injector.WhenCalled(typeof(FooClass).GetMethod(nameof(FooClass.StaticMemberFunctionReturnFalse))!)
                .WillReturn(true);

        injector.Dispose();
        injector.Dispose(); // Should not throw
    }

    [Fact]
    public void AfterDispose_NewReplacementShouldThrow()
    {
        var injector = new Injector();
        injector.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
        {
            injector.WhenCalled(typeof(FooClass).GetMethod(nameof(FooClass.StaticMemberFunctionReturnFalse))!)
                    .WillReturn(true);
        });
    }

    [Fact]
    public void WillThrow_RestoredAfterDispose()
    {
        using (var injector = new Injector())
        {
            injector.WhenCalled(typeof(FooClass).GetMethod(nameof(FooClass.StaticMemberFunctionReturnFalse))!)
                    .WillThrow<InvalidOperationException>();

            Assert.Throws<InvalidOperationException>(() => FooClass.StaticMemberFunctionReturnFalse());
        }

        // Should not throw after dispose
        Assert.False(FooClass.StaticMemberFunctionReturnFalse());
    }
}
