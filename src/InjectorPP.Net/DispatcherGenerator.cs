using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("InjectorPP_Dispatchers")]

namespace InjectorPP.Net;

/// <summary>
/// Generates dispatcher methods that perform thread-local dispatch.
/// Each dispatcher matches the original method's signature and:
/// 1. Calls ThreadLocalRegistry.GetTarget(methodKey) to get the target function pointer
/// 2. Forwards all arguments to the target via managed calli
/// </summary>
internal static class DispatcherGenerator
{
    private static readonly ModuleBuilder s_moduleBuilder;
    private static int s_typeCounter;

    private static readonly MethodInfo s_getTargetMethod =
        typeof(ThreadLocalRegistry).GetMethod(nameof(ThreadLocalRegistry.GetTarget),
            BindingFlags.Public | BindingFlags.Static)!;

    static DispatcherGenerator()
    {
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("InjectorPP_Dispatchers"),
            AssemblyBuilderAccess.Run);
        s_moduleBuilder = assemblyBuilder.DefineDynamicModule("DispatcherModule");
    }

    /// <summary>
    /// Generates a dispatcher method for the given original method.
    /// The dispatcher calls ThreadLocalRegistry.GetTarget(methodKey) and uses calli
    /// to invoke the returned function pointer with all forwarded arguments.
    /// </summary>
    public static MethodInfo GenerateDispatcher(MethodInfo original, int methodKey)
    {
        var typeName = $"Dispatcher_{Interlocked.Increment(ref s_typeCounter)}";
        var typeBuilder = s_moduleBuilder.DefineType(typeName,
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);

        var paramTypes = ReplacementGenerator.GetEffectiveParameterTypes(original);

        var methodBuilder = typeBuilder.DefineMethod("Dispatch",
            MethodAttributes.Public | MethodAttributes.Static,
            original.ReturnType,
            paramTypes);

        methodBuilder.SetImplementationFlags(MethodImplAttributes.NoInlining);

        var il = methodBuilder.GetILGenerator();

        // Load the method key and call ThreadLocalRegistry.GetTarget(key)
        il.Emit(OpCodes.Ldc_I4, methodKey);
        il.Emit(OpCodes.Call, s_getTargetMethod);

        // Store the function pointer temporarily
        var fnPtrLocal = il.DeclareLocal(typeof(IntPtr));
        il.Emit(OpCodes.Stloc, fnPtrLocal);

        // Load all arguments
        for (int i = 0; i < paramTypes.Length; i++)
        {
            EmitLdarg(il, i);
        }

        // Load the function pointer for calli
        il.Emit(OpCodes.Ldloc, fnPtrLocal);

        // Emit managed calli with the correct signature
        il.EmitCalli(OpCodes.Calli,
            CallingConventions.Standard,
            original.ReturnType,
            paramTypes,
            null);

        il.Emit(OpCodes.Ret);

        var type = typeBuilder.CreateType()!;
        return type.GetMethod("Dispatch")!;
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
