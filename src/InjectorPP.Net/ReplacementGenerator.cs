using System.Reflection;
using System.Reflection.Emit;

namespace InjectorPP.Net;

/// <summary>
/// Generates replacement methods using IL code emission.
/// This is the C# equivalent of the C++ version's hand-written ASM -
/// instead of writing platform-specific machine code, we emit platform-independent IL.
/// </summary>
internal static class ReplacementGenerator
{
    private static readonly ModuleBuilder s_moduleBuilder;
    private static int s_typeCounter;

    static ReplacementGenerator()
    {
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("InjectorPP_DynamicReplacements"),
            AssemblyBuilderAccess.Run);
        s_moduleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");
    }

    /// <summary>
    /// Generates a replacement method that returns the specified value.
    /// The generated method matches the original method's signature.
    /// </summary>
    public static MethodInfo GenerateReturnValue(MethodInfo original, object? value)
    {
        var (typeBuilder, methodBuilder) = CreateMethodBuilder(original);
        var il = methodBuilder.GetILGenerator();

        if (value != null && !original.ReturnType.IsAssignableFrom(value.GetType()))
        {
            throw new ArgumentException(
                $"Value of type '{value.GetType()}' is not assignable to return type '{original.ReturnType}'.");
        }

        EmitReturnValue(il, original.ReturnType, value, typeBuilder);

        var type = typeBuilder.CreateType()!;

        // For complex objects stored in static fields, set the field value after type creation
        if (value != null && NeedsStaticField(original.ReturnType, value))
        {
            var field = type.GetField("_returnValue", BindingFlags.Public | BindingFlags.Static);
            field?.SetValue(null, value);
        }

        return type.GetMethod("Execute")!;
    }

    /// <summary>
    /// Generates a replacement method that does nothing (returns default for non-void).
    /// </summary>
    public static MethodInfo GenerateDoNothing(MethodInfo original)
    {
        var (typeBuilder, methodBuilder) = CreateMethodBuilder(original);
        var il = methodBuilder.GetILGenerator();

        if (original.ReturnType != typeof(void))
        {
            EmitDefault(il, original.ReturnType);
        }

        il.Emit(OpCodes.Ret);

        var type = typeBuilder.CreateType()!;
        return type.GetMethod("Execute")!;
    }

    /// <summary>
    /// Generates a replacement method that throws the specified exception.
    /// </summary>
    public static MethodInfo GenerateThrow(MethodInfo original, Type exceptionType)
    {
        var (typeBuilder, methodBuilder) = CreateMethodBuilder(original);
        var il = methodBuilder.GetILGenerator();

        var ctor = exceptionType.GetConstructor(Type.EmptyTypes);
        if (ctor == null)
        {
            throw new ArgumentException(
                $"Exception type '{exceptionType}' must have a parameterless constructor.");
        }

        il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Throw);

        var type = typeBuilder.CreateType()!;
        return type.GetMethod("Execute")!;
    }

    /// <summary>
    /// Generates a replacement method that throws the specified exception instance.
    /// </summary>
    public static MethodInfo GenerateThrowInstance(MethodInfo original, Exception exception)
    {
        var (typeBuilder, methodBuilder) = CreateMethodBuilder(original);

        var exceptionField = typeBuilder.DefineField(
            "_exception", typeof(Exception),
            FieldAttributes.Public | FieldAttributes.Static);

        var il = methodBuilder.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, exceptionField);
        il.Emit(OpCodes.Throw);

        var type = typeBuilder.CreateType()!;
        type.GetField("_exception", BindingFlags.Public | BindingFlags.Static)!
            .SetValue(null, exception);

        return type.GetMethod("Execute")!;
    }

    /// <summary>
    /// Creates a static wrapper method that invokes a stored delegate.
    /// Used for WillExecute with lambda/closure delegates.
    /// </summary>
    public static MethodInfo GenerateDelegateWrapper(MethodInfo original, Delegate del)
    {
        var (typeBuilder, methodBuilder) = CreateMethodBuilder(original);

        var delegateField = typeBuilder.DefineField(
            "_delegate", del.GetType(),
            FieldAttributes.Public | FieldAttributes.Static);

        var il = methodBuilder.GetILGenerator();

        var invokeMethod = del.GetType().GetMethod("Invoke")!;
        var invokeParams = invokeMethod.GetParameters();

        // Load the delegate from the static field
        il.Emit(OpCodes.Ldsfld, delegateField);

        // For instance methods, arg0 is 'this' which the delegate doesn't expect - skip it
        int argOffset = original.IsStatic ? 0 : 1;

        for (int i = 0; i < invokeParams.Length; i++)
        {
            EmitLdarg(il, i + argOffset);
        }

        il.Emit(OpCodes.Callvirt, invokeMethod);

        if (original.ReturnType == typeof(void) && invokeMethod.ReturnType != typeof(void))
        {
            il.Emit(OpCodes.Pop);
        }

        il.Emit(OpCodes.Ret);

        var type = typeBuilder.CreateType()!;
        type.GetField("_delegate", BindingFlags.Public | BindingFlags.Static)!
            .SetValue(null, del);

        return type.GetMethod("Execute")!;
    }

    private static (TypeBuilder typeBuilder, MethodBuilder methodBuilder) CreateMethodBuilder(MethodInfo original)
    {
        var typeName = $"Replacement_{Interlocked.Increment(ref s_typeCounter)}";
        var typeBuilder = s_moduleBuilder.DefineType(typeName,
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);

        var paramTypes = GetEffectiveParameterTypes(original);

        var methodBuilder = typeBuilder.DefineMethod("Execute",
            MethodAttributes.Public | MethodAttributes.Static,
            original.ReturnType,
            paramTypes);

        methodBuilder.SetImplementationFlags(MethodImplAttributes.NoInlining);

        return (typeBuilder, methodBuilder);
    }

    /// <summary>
    /// Gets the effective parameter types for a method.
    /// For instance methods, prepends the declaring type as the first parameter
    /// to match the JIT calling convention (this pointer passed as first arg).
    /// </summary>
    internal static Type[] GetEffectiveParameterTypes(MethodInfo method)
    {
        var methodParams = method.GetParameters().Select(p => p.ParameterType).ToArray();

        if (!method.IsStatic)
        {
            var instanceType = method.DeclaringType!;
            var thisType = instanceType.IsValueType ? instanceType.MakeByRefType() : instanceType;
            return new[] { thisType }.Concat(methodParams).ToArray();
        }

        return methodParams;
    }

    private static bool NeedsStaticField(Type returnType, object value)
    {
        return returnType != typeof(bool)
            && returnType != typeof(int) && returnType != typeof(uint)
            && returnType != typeof(long) && returnType != typeof(ulong)
            && returnType != typeof(byte) && returnType != typeof(sbyte)
            && returnType != typeof(short) && returnType != typeof(ushort)
            && returnType != typeof(char)
            && returnType != typeof(float) && returnType != typeof(double)
            && returnType != typeof(string)
            && returnType != typeof(IntPtr) && returnType != typeof(UIntPtr);
    }

    private static void EmitReturnValue(ILGenerator il, Type returnType, object? value, TypeBuilder typeBuilder)
    {
        if (value == null)
        {
            if (returnType == typeof(void))
            {
                il.Emit(OpCodes.Ret);
                return;
            }

            if (returnType.IsValueType)
            {
                var local = il.DeclareLocal(returnType);
                il.Emit(OpCodes.Ldloca_S, local);
                il.Emit(OpCodes.Initobj, returnType);
                il.Emit(OpCodes.Ldloc, local);
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
            }
        }
        else if (returnType == typeof(bool))
        {
            il.Emit(OpCodes.Ldc_I4, (bool)value ? 1 : 0);
        }
        else if (returnType == typeof(int))
        {
            il.Emit(OpCodes.Ldc_I4, (int)value);
        }
        else if (returnType == typeof(uint))
        {
            il.Emit(OpCodes.Ldc_I4, unchecked((int)(uint)value));
        }
        else if (returnType == typeof(byte))
        {
            il.Emit(OpCodes.Ldc_I4, (int)(byte)value);
        }
        else if (returnType == typeof(sbyte))
        {
            il.Emit(OpCodes.Ldc_I4, (int)(sbyte)value);
        }
        else if (returnType == typeof(short))
        {
            il.Emit(OpCodes.Ldc_I4, (int)(short)value);
        }
        else if (returnType == typeof(ushort))
        {
            il.Emit(OpCodes.Ldc_I4, (int)(ushort)value);
        }
        else if (returnType == typeof(char))
        {
            il.Emit(OpCodes.Ldc_I4, (int)(char)value);
        }
        else if (returnType == typeof(long))
        {
            il.Emit(OpCodes.Ldc_I8, (long)value);
        }
        else if (returnType == typeof(ulong))
        {
            il.Emit(OpCodes.Ldc_I8, unchecked((long)(ulong)value));
        }
        else if (returnType == typeof(float))
        {
            il.Emit(OpCodes.Ldc_R4, (float)value);
        }
        else if (returnType == typeof(double))
        {
            il.Emit(OpCodes.Ldc_R8, (double)value);
        }
        else if (returnType == typeof(string))
        {
            il.Emit(OpCodes.Ldstr, (string)value);
        }
        else if (returnType == typeof(IntPtr))
        {
            il.Emit(OpCodes.Ldc_I8, ((IntPtr)value).ToInt64());
            il.Emit(OpCodes.Conv_I);
        }
        else if (returnType == typeof(UIntPtr))
        {
            il.Emit(OpCodes.Ldc_I8, unchecked((long)((UIntPtr)value).ToUInt64()));
            il.Emit(OpCodes.Conv_U);
        }
        else
        {
            // For complex types, store value in a static field and load from there
            var field = typeBuilder.DefineField("_returnValue", returnType,
                FieldAttributes.Public | FieldAttributes.Static);
            il.Emit(OpCodes.Ldsfld, field);
        }

        il.Emit(OpCodes.Ret);
    }

    private static void EmitDefault(ILGenerator il, Type returnType)
    {
        if (!returnType.IsValueType)
        {
            il.Emit(OpCodes.Ldnull);
        }
        else if (returnType == typeof(bool) || returnType == typeof(int) ||
                 returnType == typeof(uint) || returnType == typeof(byte) ||
                 returnType == typeof(sbyte) || returnType == typeof(short) ||
                 returnType == typeof(ushort) || returnType == typeof(char))
        {
            il.Emit(OpCodes.Ldc_I4_0);
        }
        else if (returnType == typeof(long) || returnType == typeof(ulong))
        {
            il.Emit(OpCodes.Ldc_I8, 0L);
        }
        else if (returnType == typeof(float))
        {
            il.Emit(OpCodes.Ldc_R4, 0f);
        }
        else if (returnType == typeof(double))
        {
            il.Emit(OpCodes.Ldc_R8, 0.0);
        }
        else
        {
            var local = il.DeclareLocal(returnType);
            il.Emit(OpCodes.Ldloca_S, local);
            il.Emit(OpCodes.Initobj, returnType);
            il.Emit(OpCodes.Ldloc, local);
        }
    }

    private static void EmitLdarg(ILGenerator il, int index)
    {
        switch (index)
        {
            case 0: il.Emit(OpCodes.Ldarg_0); break;
            case 1: il.Emit(OpCodes.Ldarg_1); break;
            case 2: il.Emit(OpCodes.Ldarg_2); break;
            case 3: il.Emit(OpCodes.Ldarg_3); break;
            default:
                if (index <= 255)
                    il.Emit(OpCodes.Ldarg_S, (byte)index);
                else
                    il.Emit(OpCodes.Ldarg, index);
                break;
        }
    }
}
