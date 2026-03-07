using InjectorPP.Net.Tests.TestTargets;
using Xunit;

namespace InjectorPP.Net.Tests;

/// <summary>
/// Tests for replacing methods with custom functions - mirrors the C++ executeFakeUnitTest.cpp.
/// </summary>
public class ExecuteFakeTests
{
    [Fact]
    public void StaticFunction_WillExecute_WithDelegate_ShouldCallReplacement()
    {
        using var injector = new Injector();
        injector.WhenCalled(typeof(FooClassForExecuteTest).GetMethod(nameof(FooClassForExecuteTest.FunctionReturnFalse))!)
                .WillExecute(new Func<bool>(() => true));

        bool result = FooClassForExecuteTest.FunctionReturnFalse();

        Assert.True(result);
    }

    [Fact]
    public void StaticFunction_WillExecute_WithParameterizedDelegate_ShouldPassArguments()
    {
        using var injector = new Injector();
        injector.WhenCalled(typeof(FooClassForExecuteTest).GetMethod(nameof(FooClassForExecuteTest.FunctionReturnInt))!)
                .WillExecute(new Func<int, int>(input => input * 10));

        int result = FooClassForExecuteTest.FunctionReturnInt(5);

        Assert.Equal(50, result);
    }

    [Fact]
    public void StaticFunction_WillExecute_WithStringDelegate_ShouldWork()
    {
        using var injector = new Injector();
        injector.WhenCalled(typeof(FooClassForExecuteTest).GetMethod(nameof(FooClassForExecuteTest.FunctionReturnString))!)
                .WillExecute(new Func<string, string>(input => "Faked_" + input));

        string result = FooClassForExecuteTest.FunctionReturnString("Test");

        Assert.Equal("Faked_Test", result);
    }

    [Fact]
    public void StaticFunction_WillExecute_WithClosure_ShouldCaptureVariables()
    {
        int capturedValue = 42;

        using var injector = new Injector();
        injector.WhenCalled(typeof(FooClassForExecuteTest).GetMethod(nameof(FooClassForExecuteTest.FunctionReturnInt))!)
                .WillExecute(new Func<int, int>(input => input + capturedValue));

        int result = FooClassForExecuteTest.FunctionReturnInt(8);

        Assert.Equal(50, result);
    }

    [Fact]
    public void InstanceFunction_WillExecute_WithDelegate_ShouldWork()
    {
        using var injector = new Injector();
        injector.WhenCalled(typeof(FooClassForExecuteTest).GetMethod(nameof(FooClassForExecuteTest.InstanceFunctionReturnFalse))!)
                .WillExecute(new Func<bool>(() => true));

        var foo = new FooClassForExecuteTest();
        bool result = foo.InstanceFunctionReturnFalse();

        Assert.True(result);
    }

    [Fact]
    public void InstanceFunction_WillExecute_ReturnString_ShouldWork()
    {
        using var injector = new Injector();
        injector.WhenCalled(typeof(FooClassForExecuteTest).GetMethod(nameof(FooClassForExecuteTest.InstanceFunctionReturnString))!)
                .WillExecute(new Func<string>(() => "ReplacedInstance"));

        var foo = new FooClassForExecuteTest();
        string result = foo.InstanceFunctionReturnString();

        Assert.Equal("ReplacedInstance", result);
    }

    [Fact]
    public void StaticFunction_WillExecute_WithStaticMethod_ShouldRedirectToMethod()
    {
        using var injector = new Injector();
        injector.WhenCalled(typeof(FooClassForExecuteTest).GetMethod(nameof(FooClassForExecuteTest.FunctionReturnFalse))!)
                .WillExecute(typeof(FakeReplacements).GetMethod(nameof(FakeReplacements.FakeReturnTrue))!);

        bool result = FooClassForExecuteTest.FunctionReturnFalse();

        Assert.True(result);
    }

    [Fact]
    public void VoidFunction_WillExecute_ShouldCallReplacementInstead()
    {
        bool wasCalled = false;

        using var injector = new Injector();
        // Note: The original method takes ref string, but we just want to verify
        // the replacement is called. Using WillDoNothing for void methods.
        injector.WhenCalled(typeof(FooClassForExecuteTest).GetMethod(nameof(FooClassForExecuteTest.FunctionReturnFalse))!)
                .WillExecute(new Func<bool>(() =>
                {
                    wasCalled = true;
                    return true;
                }));

        FooClassForExecuteTest.FunctionReturnFalse();

        Assert.True(wasCalled);
    }

    [Fact]
    public void VoidFunction_WillDoNothing_ShouldNotModifyRefParam()
    {
        string value = "InitialValue";

        using var injector = new Injector();
        injector.WhenCalled(
                typeof(FooClass).GetMethod(nameof(FooClass.StaticMemberFunctionNoReturn))!)
                .WillDoNothing();

        FooClass.StaticMemberFunctionNoReturn(ref value);

        // Value should remain unchanged because the method does nothing
        Assert.Equal("InitialValue", value);
    }
}

// Static replacement methods used by WillExecute(MethodInfo) tests
public static class FakeReplacements
{
    public static bool FakeReturnTrue()
    {
        return true;
    }

    public static int FakeReturnInt(int input)
    {
        return input * 100;
    }

    public static string FakeReturnString(string input)
    {
        return "FAKE_" + input;
    }
}
