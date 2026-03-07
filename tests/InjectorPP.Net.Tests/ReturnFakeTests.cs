using InjectorPP.Net.Tests.TestTargets;
using Xunit;

namespace InjectorPP.Net.Tests;

/// <summary>
/// Tests for faking method return values - the core feature of InjectorPP.
/// Mirrors the C++ returnFakeUnitTest.cpp patterns.
/// </summary>
[Collection("Sequential")]
public class ReturnFakeTests
{
    [Fact]
    public void StaticFunction_ReturnBool_WhenFakedToTrue_ShouldReturnTrue()
    {
        using var injector = new Injector();
        injector.WhenCalled(typeof(FooClass).GetMethod(nameof(FooClass.StaticMemberFunctionReturnFalse))!)
                .WillReturn(true);

        bool result = FooClass.StaticMemberFunctionReturnFalse();

        Assert.True(result);
    }

    [Fact]
    public void StaticFunction_ReturnInt_WhenFaked_ShouldReturnFakedValue()
    {
        using var injector = new Injector();
        injector.WhenCalled(typeof(FooClass).GetMethod(nameof(FooClass.StaticMemberFunctionReturnInt))!)
                .WillReturn(999);

        int result = FooClass.StaticMemberFunctionReturnInt();

        Assert.Equal(999, result);
    }

    [Fact]
    public void StaticFunction_ReturnString_WhenFaked_ShouldReturnFakedValue()
    {
        using var injector = new Injector();
        injector.WhenCalled(typeof(FooClass).GetMethod(nameof(FooClass.StaticMemberFunctionReturnString))!)
                .WillReturn("FakedValue");

        string result = FooClass.StaticMemberFunctionReturnString();

        Assert.Equal("FakedValue", result);
    }

    [Fact]
    public void StaticFunction_ReturnDouble_WhenFaked_ShouldReturnFakedValue()
    {
        using var injector = new Injector();
        injector.WhenCalled(typeof(FooClass).GetMethod(nameof(FooClass.StaticMemberFunctionReturnDouble))!)
                .WillReturn(2.718);

        double result = FooClass.StaticMemberFunctionReturnDouble();

        Assert.Equal(2.718, result);
    }

    [Fact]
    public void StaticFunction_ReturnLong_WhenFaked_ShouldReturnFakedValue()
    {
        using var injector = new Injector();
        injector.WhenCalled(typeof(FooClass).GetMethod(nameof(FooClass.StaticMemberFunctionReturnLong))!)
                .WillReturn(987654321L);

        long result = FooClass.StaticMemberFunctionReturnLong();

        Assert.Equal(987654321L, result);
    }

    [Fact]
    public void StaticFunction_ReturnNull_WhenFakedToNonNull_ShouldReturnFakedValue()
    {
        var expectedObj = new object();

        using var injector = new Injector();
        injector.WhenCalled(typeof(FooClass).GetMethod(nameof(FooClass.StaticMemberFunctionReturnNull))!)
                .WillReturn(expectedObj);

        object? result = FooClass.StaticMemberFunctionReturnNull();

        Assert.Same(expectedObj, result);
    }

    [Fact]
    public void InstanceFunction_ReturnBool_WhenFakedToTrue_ShouldReturnTrue()
    {
        using var injector = new Injector();
        injector.WhenCalled(
                typeof(FooClass).GetMethod(nameof(FooClass.MemberFunctionReturnFalse), Type.EmptyTypes)!)
                .WillReturn(true);

        var foo = new FooClass();
        bool result = foo.MemberFunctionReturnFalse();

        Assert.True(result);
    }

    [Fact]
    public void InstanceFunction_ReturnInt_WhenFaked_ShouldReturnFakedValue()
    {
        using var injector = new Injector();
        injector.WhenCalled(typeof(FooClass).GetMethod(nameof(FooClass.MemberFunctionReturnInt))!)
                .WillReturn(777);

        var foo = new FooClass();
        int result = foo.MemberFunctionReturnInt();

        Assert.Equal(777, result);
    }

    [Fact]
    public void InstanceFunction_ReturnString_WhenFaked_ShouldReturnFakedValue()
    {
        using var injector = new Injector();
        injector.WhenCalled(typeof(FooClass).GetMethod(nameof(FooClass.MemberFunctionReturnString))!)
                .WillReturn("FakedInstanceValue");

        var foo = new FooClass();
        string result = foo.MemberFunctionReturnString();

        Assert.Equal("FakedInstanceValue", result);
    }

    [Fact]
    public void OverloadedFunction_WithOneParam_WhenFaked_ShouldReturnFakedValue()
    {
        using var injector = new Injector();
        injector.WhenCalled(
                typeof(FooClass).GetMethod(nameof(FooClass.MemberFunctionReturnFalse), new[] { typeof(bool) })!)
                .WillReturn(true);

        var foo = new FooClass();
        bool result = foo.MemberFunctionReturnFalse(false);

        Assert.True(result);
    }

    [Fact]
    public void OverloadedFunction_WithTwoParams_WhenFaked_ShouldReturnFakedValue()
    {
        using var injector = new Injector();
        injector.WhenCalled(
                typeof(FooClass).GetMethod(nameof(FooClass.MemberFunctionReturnFalse),
                    new[] { typeof(bool), typeof(bool) })!)
                .WillReturn(true);

        var foo = new FooClass();
        bool result = foo.MemberFunctionReturnFalse(false, false);

        Assert.True(result);
    }

    [Fact]
    public void StaticFunction_WillDoNothing_ShouldReturnDefault()
    {
        using var injector = new Injector();
        injector.WhenCalled(typeof(FooClass).GetMethod(nameof(FooClass.StaticMemberFunctionReturnInt))!)
                .WillDoNothing();

        int result = FooClass.StaticMemberFunctionReturnInt();

        Assert.Equal(0, result);
    }

    [Fact]
    public void StaticFunction_WillDoNothing_BoolMethod_ShouldReturnFalse()
    {
        using var injector = new Injector();
        // First fake it to true, then override with DoNothing (returns default=false)
        injector.WhenCalled(typeof(FooClass).GetMethod(nameof(FooClass.StaticMemberFunctionReturnFalse))!)
                .WillDoNothing();

        bool result = FooClass.StaticMemberFunctionReturnFalse();

        Assert.False(result);
    }

    [Fact]
    public void StaticFunction_WillThrow_ShouldThrowSpecifiedException()
    {
        using var injector = new Injector();
        injector.WhenCalled(typeof(FooClass).GetMethod(nameof(FooClass.StaticMemberFunctionReturnFalse))!)
                .WillThrow<InvalidOperationException>();

        Assert.Throws<InvalidOperationException>(() => FooClass.StaticMemberFunctionReturnFalse());
    }

    [Fact]
    public void StaticFunction_WillThrowInstance_ShouldThrowExactExceptionInstance()
    {
        var expectedException = new ArgumentException("Test error message");

        using var injector = new Injector();
        injector.WhenCalled(typeof(FooClass).GetMethod(nameof(FooClass.StaticMemberFunctionReturnInt))!)
                .WillThrow(expectedException);

        var ex = Assert.Throws<ArgumentException>(() => FooClass.StaticMemberFunctionReturnInt());
        Assert.Equal("Test error message", ex.Message);
    }

    [Fact]
    public void StaticFunction_UsingExpressionSyntax_ShouldWork()
    {
        using var injector = new Injector();
        injector.WhenCalled(() => FooClass.StaticMemberFunctionReturnFalse())
                .WillReturn(true);

        bool result = FooClass.StaticMemberFunctionReturnFalse();

        Assert.True(result);
    }

    [Fact]
    public void ComplexReturnType_WhenFaked_ShouldReturnFakedObject()
    {
        var expectedList = new List<int> { 10, 20, 30 };

        using var injector = new Injector();
        injector.WhenCalled(typeof(ComplexReturnClass).GetMethod(nameof(ComplexReturnClass.GetList))!)
                .WillReturn(expectedList);

        var result = ComplexReturnClass.GetList();

        Assert.Same(expectedList, result);
    }

    [Fact]
    public void ComplexReturnType_WhenFakedToNull_ShouldReturnNull()
    {
        using var injector = new Injector();
        injector.WhenCalled(typeof(ComplexReturnClass).GetMethod(nameof(ComplexReturnClass.GetList))!)
                .WillReturn<List<int>?>(null);

        var result = ComplexReturnClass.GetList();

        Assert.Null(result);
    }

    [Fact]
    public void RealWorldScenario_VerifyCert_ShouldAllowTesting()
    {
        // This demonstrates the core value of injectorpp:
        // Testing code that depends on VerifyCertInMachine() without changing the production code.
        using var injector = new Injector();
        injector.WhenCalled(typeof(CertValidator).GetMethod(nameof(CertValidator.VerifyCertInMachine))!)
                .WillReturn(true);

        int result = CertValidator.MarkProcessValidated(true);

        Assert.Equal(0, result);
    }

    [Fact]
    public void RealWorldScenario_VerifyCertFails_ShouldReturnError()
    {
        using var injector = new Injector();
        injector.WhenCalled(typeof(CertValidator).GetMethod(nameof(CertValidator.VerifyCertInMachine))!)
                .WillReturn(false);

        int result = CertValidator.MarkProcessValidated(true);

        Assert.Equal(-2, result);
    }

    [Fact]
    public void PropertyGetter_WhenFaked_ShouldReturnFakedValue()
    {
        using var injector = new Injector();
        var getter = typeof(PropertyClass).GetProperty(nameof(PropertyClass.Name))!.GetGetMethod()!;
        injector.WhenCalled(getter).WillReturn("FakedName");

        var obj = new PropertyClass();
        Assert.Equal("FakedName", obj.Name);
    }

    [Fact]
    public void StaticPropertyGetter_WhenFaked_ShouldReturnFakedValue()
    {
        using var injector = new Injector();
        var getter = typeof(PropertyClass).GetProperty(nameof(PropertyClass.StaticValue))!.GetGetMethod()!;
        injector.WhenCalled(getter).WillReturn(100);

        Assert.Equal(100, PropertyClass.StaticValue);
    }
}
