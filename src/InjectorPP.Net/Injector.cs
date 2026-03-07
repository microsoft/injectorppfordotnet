using System.Linq.Expressions;
using System.Reflection;

namespace InjectorPP.Net;

/// <summary>
/// Main entry point for InjectorPP.Net. Allows replacing method behavior at runtime
/// for unit testing purposes, without requiring code changes or interface abstractions.
///
/// Usage:
/// <code>
/// using var injector = new Injector();
/// injector.WhenCalled(typeof(CertValidator).GetMethod("VerifyCert")!)
///         .WillReturn(true);
///
/// // VerifyCert() will now return true until the injector is disposed.
/// </code>
///
/// The injector implements IDisposable - when disposed, all original method behaviors
/// are restored (similar to the C++ version's destructor-based RAII pattern).
/// </summary>
public sealed class Injector : IDisposable
{
    private readonly List<MethodReplacement> _replacements = new();
    private bool _disposed;

    /// <summary>
    /// Specifies a method to be replaced, identified by MethodInfo.
    /// Returns a builder for configuring the replacement behavior.
    /// </summary>
    public InjectionBuilder WhenCalled(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return new InjectionBuilder(this, method);
    }

    /// <summary>
    /// Specifies a static void method to be replaced, using a lambda expression for discovery.
    /// Example: injector.WhenCalled(() => MyClass.StaticMethod())
    /// </summary>
    public InjectionBuilder WhenCalled(Expression<Action> methodExpression)
    {
        var method = ExtractMethodInfo(methodExpression.Body);
        return new InjectionBuilder(this, method);
    }

    /// <summary>
    /// Specifies a static method with a return value to be replaced, using a lambda expression.
    /// Example: injector.WhenCalled(() => MyClass.StaticMethod())
    /// </summary>
    public InjectionBuilder WhenCalled<TResult>(Expression<Func<TResult>> methodExpression)
    {
        var method = ExtractMethodInfo(methodExpression.Body);
        return new InjectionBuilder(this, method);
    }

    /// <summary>
    /// Specifies a non-public method to be replaced, by type and method name.
    /// Uses reflection with all binding flags to find the method.
    /// Example: injector.WhenCalled&lt;MyClass&gt;("PrivateMethod")
    /// </summary>
    public InjectionBuilder WhenCalled<T>(string methodName, params Type[] parameterTypes)
    {
        return WhenCalled(typeof(T), methodName, parameterTypes);
    }

    /// <summary>
    /// Specifies a non-public method to be replaced, by type and method name.
    /// </summary>
    public InjectionBuilder WhenCalled(Type type, string methodName, params Type[] parameterTypes)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(methodName);

        const BindingFlags allFlags =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static;

        MethodInfo? method;
        if (parameterTypes.Length > 0)
        {
            method = type.GetMethod(methodName, allFlags, null, parameterTypes, null);
        }
        else
        {
            method = type.GetMethod(methodName, allFlags);
        }

        if (method == null)
        {
            throw new ArgumentException(
                $"Method '{methodName}' not found on type '{type.FullName}'.");
        }

        return new InjectionBuilder(this, method);
    }

    /// <summary>
    /// Registers a method replacement. Called by InjectionBuilder.
    /// </summary>
    internal void RegisterReplacement(MethodInfo originalMethod, MethodInfo replacementMethod)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var replacement = MethodReplacer.Replace(originalMethod, replacementMethod);
        _replacements.Add(replacement);
    }

    /// <summary>
    /// Restores all replaced methods to their original behavior.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Restore in reverse order to handle nested replacements correctly
        for (int i = _replacements.Count - 1; i >= 0; i--)
        {
            MethodReplacer.Restore(_replacements[i]);
        }

        _replacements.Clear();
    }

    private static MethodInfo ExtractMethodInfo(Expression body)
    {
        if (body is MethodCallExpression methodCall)
        {
            return methodCall.Method;
        }

        if (body is UnaryExpression { Operand: MethodCallExpression innerCall })
        {
            return innerCall.Method;
        }

        throw new ArgumentException(
            "Expression must be a method call expression. " +
            "Example: () => MyClass.SomeMethod()");
    }
}
