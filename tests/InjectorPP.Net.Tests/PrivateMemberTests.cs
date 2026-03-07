using InjectorPP.Net.Tests.TestTargets;
using Xunit;

namespace InjectorPP.Net.Tests;

/// <summary>
/// Tests for accessing and faking non-public (private/protected) members.
/// In C#, reflection makes this much simpler than the C++ accessor macro approach.
/// </summary>
public class PrivateMemberTests
{
    [Fact]
    public void PrivateInstanceMethod_WhenFaked_ShouldReturnFakedValue()
    {
        using var injector = new Injector();
        injector.WhenCalled<FooClass>("PrivateMemberFunctionReturnFalse")
                .WillReturn(true);

        var foo = new FooClass();
        bool result = foo.CallPrivateMethod();

        Assert.True(result);
    }

    [Fact]
    public void PrivateInstanceMethod_ReturnInt_WhenFaked_ShouldReturnFakedValue()
    {
        using var injector = new Injector();
        injector.WhenCalled<FooClass>("PrivateMemberFunctionReturnInt")
                .WillReturn(42);

        var foo = new FooClass();
        int result = foo.CallPrivateIntMethod();

        Assert.Equal(42, result);
    }

    [Fact]
    public void ProtectedInstanceMethod_WhenFaked_ShouldReturnFakedValue()
    {
        using var injector = new Injector();
        injector.WhenCalled<FooClass>("ProtectedMemberFunctionReturnFalse")
                .WillReturn(true);

        var foo = new FooClass();
        bool result = foo.CallProtectedMethod();

        Assert.True(result);
    }

    [Fact]
    public void PrivateStaticMethod_WhenFaked_ShouldReturnFakedValue()
    {
        using var injector = new Injector();
        injector.WhenCalled<FooClass>("PrivateStaticMemberFunctionReturnFalse")
                .WillReturn(true);

        var foo = new FooClass();
        bool result = foo.CallPrivateStaticMethod();

        Assert.True(result);
    }

    [Fact]
    public void PrivateMethod_UsingTypeAndName_ShouldWork()
    {
        using var injector = new Injector();
        injector.WhenCalled(typeof(FooClass), "PrivateMemberFunctionReturnFalse")
                .WillReturn(true);

        var foo = new FooClass();
        bool result = foo.CallPrivateMethod();

        Assert.True(result);
    }

    [Fact]
    public void NonExistentMethod_ShouldThrowArgumentException()
    {
        using var injector = new Injector();

        Assert.Throws<ArgumentException>(() =>
        {
            injector.WhenCalled<FooClass>("MethodThatDoesNotExist");
        });
    }

    [Fact]
    public void PrivateMethod_WillThrow_ShouldThrowSpecifiedException()
    {
        using var injector = new Injector();
        injector.WhenCalled<FooClass>("PrivateMemberFunctionReturnFalse")
                .WillThrow<InvalidOperationException>();

        var foo = new FooClass();

        Assert.Throws<InvalidOperationException>(() => foo.CallPrivateMethod());
    }

    [Fact]
    public void PrivateMethod_RestoredAfterDispose()
    {
        var foo = new FooClass();
        Assert.False(foo.CallPrivateMethod());

        using (var injector = new Injector())
        {
            injector.WhenCalled<FooClass>("PrivateMemberFunctionReturnFalse")
                    .WillReturn(true);

            Assert.True(foo.CallPrivateMethod());
        }

        Assert.False(foo.CallPrivateMethod());
    }

    [Fact]
    public void PrivateMethod_WillDoNothing_ShouldReturnDefault()
    {
        using var injector = new Injector();
        injector.WhenCalled<FooClass>("PrivateMemberFunctionReturnInt")
                .WillDoNothing();

        var foo = new FooClass();
        int result = foo.CallPrivateIntMethod();

        Assert.Equal(0, result);
    }
}
