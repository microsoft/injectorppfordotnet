using System.Reflection;

namespace InjectorPP.Net;

/// <summary>
/// Fluent builder for configuring method injection behavior.
/// Returned by <see cref="Injector.WhenCalled"/>.
/// </summary>
public sealed class InjectionBuilder
{
    private readonly Injector _injector;
    private readonly MethodInfo _targetMethod;

    internal InjectionBuilder(Injector injector, MethodInfo targetMethod)
    {
        _injector = injector;
        _targetMethod = targetMethod;
    }

    /// <summary>
    /// Makes the target method return the specified value.
    /// Supports bool, int, long, float, double, string, and reference types.
    /// </summary>
    public InjectionBuilder WillReturn<T>(T value)
    {
        var replacement = ReplacementGenerator.GenerateReturnValue(_targetMethod, value);
        _injector.RegisterReplacement(_targetMethod, replacement);
        return this;
    }

    /// <summary>
    /// Makes the target method return the specified value (non-generic overload).
    /// </summary>
    public InjectionBuilder WillReturn(object? value)
    {
        var replacement = ReplacementGenerator.GenerateReturnValue(_targetMethod, value);
        _injector.RegisterReplacement(_targetMethod, replacement);
        return this;
    }

    /// <summary>
    /// Makes the target method do nothing and return the default value for its return type.
    /// For void methods, simply returns immediately.
    /// </summary>
    public InjectionBuilder WillDoNothing()
    {
        var replacement = ReplacementGenerator.GenerateDoNothing(_targetMethod);
        _injector.RegisterReplacement(_targetMethod, replacement);
        return this;
    }

    /// <summary>
    /// Replaces the target method with a custom delegate.
    /// The delegate signature should match the original method's parameters (excluding 'this').
    /// </summary>
    public InjectionBuilder WillExecute(Delegate replacement)
    {
        if (replacement.Target == null && IsSignatureCompatible(replacement.Method))
        {
            // Static method with compatible signature - use directly
            _injector.RegisterReplacement(_targetMethod, replacement.Method);
        }
        else
        {
            // Lambda/closure or incompatible signature - create a wrapper
            var wrapper = ReplacementGenerator.GenerateDelegateWrapper(_targetMethod, replacement);
            _injector.RegisterReplacement(_targetMethod, wrapper);
        }
        return this;
    }

    /// <summary>
    /// Replaces the target method with another specific method.
    /// The replacement method must be static and have a compatible signature.
    /// For instance methods, the replacement must include the instance type as the first parameter.
    /// </summary>
    public InjectionBuilder WillExecute(MethodInfo replacement)
    {
        if (!replacement.IsStatic)
        {
            throw new ArgumentException(
                "Replacement method must be static. For instance method replacements, " +
                "add the instance type as the first parameter.");
        }

        _injector.RegisterReplacement(_targetMethod, replacement);
        return this;
    }

    /// <summary>
    /// Makes the target method throw an exception of the specified type.
    /// The exception type must have a parameterless constructor.
    /// </summary>
    public InjectionBuilder WillThrow<TException>() where TException : Exception, new()
    {
        var replacement = ReplacementGenerator.GenerateThrow(_targetMethod, typeof(TException));
        _injector.RegisterReplacement(_targetMethod, replacement);
        return this;
    }

    /// <summary>
    /// Makes the target method throw the specified exception instance.
    /// </summary>
    public InjectionBuilder WillThrow(Exception exception)
    {
        var replacement = ReplacementGenerator.GenerateThrowInstance(_targetMethod, exception);
        _injector.RegisterReplacement(_targetMethod, replacement);
        return this;
    }

    private bool IsSignatureCompatible(MethodInfo candidateMethod)
    {
        if (!candidateMethod.IsStatic) return false;

        var expectedParams = ReplacementGenerator.GetEffectiveParameterTypes(_targetMethod);
        var candidateParams = candidateMethod.GetParameters().Select(p => p.ParameterType).ToArray();

        if (expectedParams.Length != candidateParams.Length) return false;

        for (int i = 0; i < expectedParams.Length; i++)
        {
            if (expectedParams[i] != candidateParams[i]) return false;
        }

        return _targetMethod.ReturnType == candidateMethod.ReturnType;
    }
}
