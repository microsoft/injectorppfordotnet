namespace InjectorPP.Net.Tests.TestTargets;

// Sample classes used as test targets for InjectorPP.Net tests.
// These mirror the C++ FooClass/DerivedFooClass pattern from the original injectorpp.

public class FooClass
{
    public static bool StaticMemberFunctionReturnFalse()
    {
        return false;
    }

    public static int StaticMemberFunctionReturnInt()
    {
        return 42;
    }

    public static string StaticMemberFunctionReturnString()
    {
        return "OriginalValue";
    }

    public static double StaticMemberFunctionReturnDouble()
    {
        return 3.14;
    }

    public static long StaticMemberFunctionReturnLong()
    {
        return 123456789L;
    }

    public static object? StaticMemberFunctionReturnNull()
    {
        return null;
    }

    public static void StaticMemberFunctionNoReturn(ref string value)
    {
        value = "ModifiedByOriginal";
    }

    public bool MemberFunctionReturnFalse()
    {
        return false;
    }

    public bool MemberFunctionReturnFalse(bool a)
    {
        return a && false;
    }

    public bool MemberFunctionReturnFalse(bool a, bool b)
    {
        return a && b && false;
    }

    public int MemberFunctionReturnInt()
    {
        return 100;
    }

    public string MemberFunctionReturnString()
    {
        return "InstanceOriginal";
    }

    public void MemberFunctionNoReturn(ref string value)
    {
        value = "NewValue";
    }

    public virtual byte VirtualMemberFunctionReturnByte()
    {
        return 1;
    }

    public bool CallPrivateMethod()
    {
        return PrivateMemberFunctionReturnFalse();
    }

    public int CallPrivateIntMethod()
    {
        return PrivateMemberFunctionReturnInt();
    }

    public bool CallProtectedMethod()
    {
        return ProtectedMemberFunctionReturnFalse();
    }

    public bool CallPrivateStaticMethod()
    {
        return PrivateStaticMemberFunctionReturnFalse();
    }

    private bool PrivateMemberFunctionReturnFalse()
    {
        return false;
    }

    private int PrivateMemberFunctionReturnInt()
    {
        return 0;
    }

    protected bool ProtectedMemberFunctionReturnFalse()
    {
        return false;
    }

    private static bool PrivateStaticMemberFunctionReturnFalse()
    {
        return false;
    }
}

public class DerivedFooClass : FooClass
{
    public override byte VirtualMemberFunctionReturnByte()
    {
        return 2;
    }
}

public class ComplexReturnClass
{
    public static List<int> GetList()
    {
        return new List<int> { 1, 2, 3 };
    }

    public static FooClass? GetFooClass()
    {
        return null;
    }

    public string GetName()
    {
        return "Original";
    }
}

// Simulates a class with external dependencies that are hard to test.
// This is the kind of code injectorpp is designed to help test.
public static class CertValidator
{
    public static bool VerifyCertInMachine()
    {
        // Simulates checking a real certificate - normally hard to test
        return false;
    }

    public static int MarkProcessValidated(bool processExists)
    {
        if (!processExists)
            return -1;

        bool isSuccess = VerifyCertInMachine();
        if (!isSuccess)
            return -2;

        return 0;
    }
}

public class FooClassForExecuteTest
{
    public static bool FunctionReturnFalse()
    {
        return false;
    }

    public static int FunctionReturnInt(int input)
    {
        return input * 2;
    }

    public static string FunctionReturnString(string input)
    {
        return "Original_" + input;
    }

    public static void FunctionWithRefParameter(ref string output)
    {
        output = "OriginalOutput";
    }

    public bool InstanceFunctionReturnFalse()
    {
        return false;
    }

    public string InstanceFunctionReturnString()
    {
        return "OriginalInstance";
    }

    public void InstanceFunctionNoReturn(ref string output)
    {
        output = "OriginalInstanceOutput";
    }
}

// Target classes for thread safety tests — isolated from other test targets
// to ensure clean, independent thread-safety verification.

public static class ThreadSafeTarget
{
    public static int GetValue() => -1;

    public static string GetName() => "Original";

    public static bool IsEnabled() => false;

    public static void DoSomething() { }

    public static int GetOtherValue() => -2;

    public static string GetDescription() => "Default";
}

public class ThreadSafeInstanceTarget
{
    public int GetValue() => -1;

    public string GetName() => "InstanceOriginal";
}

public class PropertyClass
{
    private string _name = "OriginalName";

    public string Name
    {
        get => _name;
        set => _name = value;
    }

    public static int StaticValue
    {
        get => 42;
    }
}
