using System.Runtime.CompilerServices;

namespace InjectorPP.Net.Tests.TestTargets;

// Sample classes used as test targets for InjectorPP.Net tests.
// These mirror the C++ FooClass/DerivedFooClass pattern from the original injectorpp.

public class FooClass
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool StaticMemberFunctionReturnFalse()
    {
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int StaticMemberFunctionReturnInt()
    {
        return 42;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string StaticMemberFunctionReturnString()
    {
        return "OriginalValue";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static double StaticMemberFunctionReturnDouble()
    {
        return 3.14;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static long StaticMemberFunctionReturnLong()
    {
        return 123456789L;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? StaticMemberFunctionReturnNull()
    {
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void StaticMemberFunctionNoReturn(ref string value)
    {
        value = "ModifiedByOriginal";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool MemberFunctionReturnFalse()
    {
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool MemberFunctionReturnFalse(bool a)
    {
        return a && false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool MemberFunctionReturnFalse(bool a, bool b)
    {
        return a && b && false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int MemberFunctionReturnInt()
    {
        return 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public string MemberFunctionReturnString()
    {
        return "InstanceOriginal";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void MemberFunctionNoReturn(ref string value)
    {
        value = "NewValue";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual byte VirtualMemberFunctionReturnByte()
    {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool CallPrivateMethod()
    {
        return PrivateMemberFunctionReturnFalse();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int CallPrivateIntMethod()
    {
        return PrivateMemberFunctionReturnInt();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool CallProtectedMethod()
    {
        return ProtectedMemberFunctionReturnFalse();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool CallPrivateStaticMethod()
    {
        return PrivateStaticMemberFunctionReturnFalse();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool PrivateMemberFunctionReturnFalse()
    {
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int PrivateMemberFunctionReturnInt()
    {
        return 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    protected bool ProtectedMemberFunctionReturnFalse()
    {
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool PrivateStaticMemberFunctionReturnFalse()
    {
        return false;
    }
}

public class DerivedFooClass : FooClass
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public override byte VirtualMemberFunctionReturnByte()
    {
        return 2;
    }
}

public class ComplexReturnClass
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static List<int> GetList()
    {
        return new List<int> { 1, 2, 3 };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static FooClass? GetFooClass()
    {
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public string GetName()
    {
        return "Original";
    }
}

// Simulates a class with external dependencies that are hard to test.
// This is the kind of code injectorpp is designed to help test.
public static class CertValidator
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool VerifyCertInMachine()
    {
        // Simulates checking a real certificate - normally hard to test
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
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
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool FunctionReturnFalse()
    {
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int FunctionReturnInt(int input)
    {
        return input * 2;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string FunctionReturnString(string input)
    {
        return "Original_" + input;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void FunctionWithRefParameter(ref string output)
    {
        output = "OriginalOutput";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool InstanceFunctionReturnFalse()
    {
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public string InstanceFunctionReturnString()
    {
        return "OriginalInstance";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void InstanceFunctionNoReturn(ref string output)
    {
        output = "OriginalInstanceOutput";
    }
}

public class PropertyClass
{
    private string _name = "OriginalName";

    public string Name
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        get => _name;
        [MethodImpl(MethodImplOptions.NoInlining)]
        set => _name = value;
    }

    public static int StaticValue
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        get => 42;
    }
}
