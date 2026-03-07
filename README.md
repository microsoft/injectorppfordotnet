# InjectorPP.Net

**Stop writing interfaces just to make your code testable.** InjectorPP.Net is a runtime method replacement library for C# that lets you unit test any code — without changing it.

## The Problem

In the C# world, the conventional path to testability looks like this:

```csharp
// Step 1: You have simple, working production code
public class OrderService
{
    public bool ProcessOrder(Order order)
    {
        bool isValid = CertValidator.VerifyCertInMachine();
        if (!isValid) return false;

        PaymentGateway.Charge(order.Amount);
        return true;
    }
}
```

```csharp
// Step 2: To "make it testable", you add interfaces, constructors, and wiring
public interface ICertValidator { bool VerifyCertInMachine(); }
public interface IPaymentGateway { void Charge(decimal amount); }

public class OrderService
{
    private readonly ICertValidator _certValidator;
    private readonly IPaymentGateway _paymentGateway;

    public OrderService(ICertValidator certValidator, IPaymentGateway paymentGateway)
    {
        _certValidator = certValidator;
        _paymentGateway = paymentGateway;
    }

    public bool ProcessOrder(Order order)
    {
        bool isValid = _certValidator.VerifyCertInMachine();
        if (!isValid) return false;

        _paymentGateway.Charge(order.Amount);
        return true;
    }
}
```

What started as 10 lines became 20+. Multiply this across hundreds of classes and you get:
- **Interfaces that exist only for testing** — never implemented more than once
- **Constructor bloat** — services with 5, 10, 15 injected dependencies
- **DI container complexity** — wiring, registration, lifetime management
- **Indirection everywhere** — harder to read, navigate, and debug

The production code got *worse* to support the tests. That's backwards.

## The Solution

InjectorPP.Net takes a completely different approach. Instead of restructuring your code around dependency injection, it **replaces method behavior at runtime** — so your production code stays exactly as it was:

```csharp
// Test code: just fake the dependencies and test ProcessOrder directly
[Fact]
public void ProcessOrder_WhenCertIsValid_ShouldSucceed()
{
    using var injector = new Injector();
    injector.WhenCalled(typeof(CertValidator).GetMethod(nameof(CertValidator.VerifyCertInMachine))!)
            .WillReturn(true);
    injector.WhenCalled(typeof(PaymentGateway).GetMethod(nameof(PaymentGateway.Charge))!)
            .WillDoNothing();

    var service = new OrderService();
    bool result = service.ProcessOrder(order);

    Assert.True(result);  // Success path tested without real certificate or payment
}
```

**No interfaces. No constructor injection. No DI container. No code changes.**

## Installation

```
dotnet add package InjectorPP.Net
```

**Requirements:** .NET 8.0+ | Windows or Linux | x64, ARM64, or x86

## Use Cases

- [Fake Return Values](#fake-return-values)
- [Fake with Custom Logic](#fake-with-custom-logic)
- [Throw Exceptions](#throw-exceptions)
- [Make Methods Do Nothing](#make-methods-do-nothing)
- [Mock Private and Protected Methods](#mock-private-and-protected-methods)
- [Mock Properties](#mock-properties)
- [Mock Overloaded Methods](#mock-overloaded-methods)
- [Scoping and Automatic Cleanup](#scoping-and-automatic-cleanup)

---

### Fake Return Values

The most common use case — force a method to return whatever you need for your test.

#### Static methods

```csharp
using var injector = new Injector();

// Bool
injector.WhenCalled(typeof(MyClass).GetMethod(nameof(MyClass.IsFeatureEnabled))!)
        .WillReturn(true);

// Int
injector.WhenCalled(typeof(MyClass).GetMethod(nameof(MyClass.GetRetryCount))!)
        .WillReturn(999);

// String
injector.WhenCalled(typeof(MyClass).GetMethod(nameof(MyClass.GetConnectionString))!)
        .WillReturn("Server=test;Database=mock");

// Double
injector.WhenCalled(typeof(MyClass).GetMethod(nameof(MyClass.GetTimeout))!)
        .WillReturn(2.718);

// Long
injector.WhenCalled(typeof(MyClass).GetMethod(nameof(MyClass.GetTimestamp))!)
        .WillReturn(987654321L);

// Complex objects
var fakeList = new List<int> { 10, 20, 30 };
injector.WhenCalled(typeof(MyClass).GetMethod(nameof(MyClass.GetItems))!)
        .WillReturn(fakeList);

// Null
injector.WhenCalled(typeof(MyClass).GetMethod(nameof(MyClass.GetItems))!)
        .WillReturn<List<int>?>(null);
```

#### Instance methods

```csharp
using var injector = new Injector();
injector.WhenCalled(typeof(MyService).GetMethod(nameof(MyService.IsConnected))!)
        .WillReturn(true);

var service = new MyService();
bool connected = service.IsConnected();  // Returns true
```

#### Expression syntax (cleaner for static methods)

```csharp
using var injector = new Injector();
injector.WhenCalled(() => MyClass.IsFeatureEnabled())
        .WillReturn(true);
```

---

### Fake with Custom Logic

Replace a method with your own delegate when you need more than a static return value.

#### Simple delegate

```csharp
using var injector = new Injector();
injector.WhenCalled(typeof(Calculator).GetMethod(nameof(Calculator.Compute))!)
        .WillExecute(new Func<int, int>(input => input * 10));

int result = Calculator.Compute(5);  // Returns 50 instead of the real computation
```

#### String transformation

```csharp
using var injector = new Injector();
injector.WhenCalled(typeof(Formatter).GetMethod(nameof(Formatter.Format))!)
        .WillExecute(new Func<string, string>(input => "Mock_" + input));

string result = Formatter.Format("Test");  // Returns "Mock_Test"
```

#### Delegate with closure (captured variables)

```csharp
int callCount = 0;

using var injector = new Injector();
injector.WhenCalled(typeof(MyService).GetMethod(nameof(MyService.Process))!)
        .WillExecute(new Func<int, int>(input =>
        {
            callCount++;
            return input + 42;
        }));

MyService.Process(8);   // Returns 50, callCount = 1
MyService.Process(8);   // Returns 50, callCount = 2
```

#### Redirect to another method

```csharp
public static class FakeImplementation
{
    public static bool AlwaysTrue() => true;
}

using var injector = new Injector();
injector.WhenCalled(typeof(RealService).GetMethod(nameof(RealService.CheckStatus))!)
        .WillExecute(typeof(FakeImplementation).GetMethod(nameof(FakeImplementation.AlwaysTrue))!);
```

#### Verify a method was called

```csharp
bool wasCalled = false;

using var injector = new Injector();
injector.WhenCalled(typeof(Logger).GetMethod(nameof(Logger.Log))!)
        .WillExecute(new Func<bool>(() =>
        {
            wasCalled = true;
            return true;
        }));

// Run your code...
Assert.True(wasCalled);
```

---

### Throw Exceptions

Test error handling paths by making methods throw.

#### By exception type

```csharp
using var injector = new Injector();
injector.WhenCalled(typeof(Database).GetMethod(nameof(Database.Connect))!)
        .WillThrow<InvalidOperationException>();

Assert.Throws<InvalidOperationException>(() => Database.Connect());
```

#### With a specific exception instance

```csharp
var exception = new ArgumentException("Connection string is invalid");

using var injector = new Injector();
injector.WhenCalled(typeof(Database).GetMethod(nameof(Database.Connect))!)
        .WillThrow(exception);

var ex = Assert.Throws<ArgumentException>(() => Database.Connect());
Assert.Equal("Connection string is invalid", ex.Message);
```

---

### Make Methods Do Nothing

Neutralize side effects — the method returns `default` for its type (`false`, `0`, `null`, etc.).

```csharp
using var injector = new Injector();

// Int method returns 0
injector.WhenCalled(typeof(MyClass).GetMethod(nameof(MyClass.GetCount))!)
        .WillDoNothing();
Assert.Equal(0, MyClass.GetCount());

// Bool method returns false
injector.WhenCalled(typeof(MyClass).GetMethod(nameof(MyClass.IsReady))!)
        .WillDoNothing();
Assert.False(MyClass.IsReady());

// Void method with ref param — param is not modified
string value = "Original";
injector.WhenCalled(typeof(MyClass).GetMethod(nameof(MyClass.Modify))!)
        .WillDoNothing();
MyClass.Modify(ref value);
Assert.Equal("Original", value);
```

---

### Mock Private and Protected Methods

No need to make methods `internal` or use `[InternalsVisibleTo]`. Mock them directly.

#### Private instance method

```csharp
using var injector = new Injector();
injector.WhenCalled<MyClass>("PrivateValidate")
        .WillReturn(true);

var obj = new MyClass();
// When MyClass internally calls PrivateValidate(), it now returns true
```

#### Private static method

```csharp
using var injector = new Injector();
injector.WhenCalled<MyClass>("PrivateStaticHelper")
        .WillReturn(true);
```

#### Protected method

```csharp
using var injector = new Injector();
injector.WhenCalled<MyClass>("ProtectedOnInitialize")
        .WillReturn(true);
```

#### Alternative syntax (using Type)

```csharp
using var injector = new Injector();
injector.WhenCalled(typeof(MyClass), "PrivateValidate")
        .WillReturn(true);
```

#### Throw from private method

```csharp
using var injector = new Injector();
injector.WhenCalled<MyClass>("PrivateValidate")
        .WillThrow<InvalidOperationException>();
```

---

### Mock Properties

Mock property getters and setters via their underlying methods.

#### Instance property getter

```csharp
using var injector = new Injector();
var getter = typeof(MyClass).GetProperty(nameof(MyClass.Name))!.GetGetMethod()!;
injector.WhenCalled(getter).WillReturn("MockedName");

var obj = new MyClass();
Assert.Equal("MockedName", obj.Name);
```

#### Static property getter

```csharp
using var injector = new Injector();
var getter = typeof(AppConfig).GetProperty(nameof(AppConfig.MaxRetries))!.GetGetMethod()!;
injector.WhenCalled(getter).WillReturn(100);

Assert.Equal(100, AppConfig.MaxRetries);
```

---

### Mock Overloaded Methods

Target specific overloads by specifying parameter types.

```csharp
using var injector = new Injector();

// Mock the overload with one bool parameter
injector.WhenCalled(
        typeof(MyClass).GetMethod(nameof(MyClass.Process), new[] { typeof(bool) })!)
        .WillReturn(true);

// Mock the overload with two bool parameters
injector.WhenCalled(
        typeof(MyClass).GetMethod(nameof(MyClass.Process), new[] { typeof(bool), typeof(bool) })!)
        .WillReturn(true);
```

---

### Scoping and Automatic Cleanup

InjectorPP.Net implements `IDisposable`. When the injector is disposed, **all original method behaviors are automatically restored**. No test pollution.

#### Using statement (recommended)

```csharp
// Original behavior
Assert.Equal(42, Calculator.GetAnswer());

using (var injector = new Injector())
{
    injector.WhenCalled(typeof(Calculator).GetMethod(nameof(Calculator.GetAnswer))!)
            .WillReturn(999);

    Assert.Equal(999, Calculator.GetAnswer());  // Mocked
}

Assert.Equal(42, Calculator.GetAnswer());  // Restored automatically
```

#### Multiple replacements — all restored

```csharp
using (var injector = new Injector())
{
    injector.WhenCalled(typeof(MyClass).GetMethod(nameof(MyClass.GetA))!).WillReturn(true);
    injector.WhenCalled(typeof(MyClass).GetMethod(nameof(MyClass.GetB))!).WillReturn(999);
    injector.WhenCalled(typeof(MyClass).GetMethod(nameof(MyClass.GetC))!).WillReturn("Fake");

    // All three methods are mocked inside this scope
}

// All three methods are back to their original behavior
```

#### Dispose is idempotent

```csharp
var injector = new Injector();
injector.WhenCalled(...).WillReturn(true);

injector.Dispose();
injector.Dispose();  // Safe — no exception
```

#### Post-dispose safety

```csharp
var injector = new Injector();
injector.Dispose();

// Attempting to register new replacements after dispose throws
Assert.Throws<ObjectDisposedException>(() =>
{
    injector.WhenCalled(...).WillReturn(true);
});
```

---

## Real-World Example

Here's the scenario InjectorPP.Net was built for. Your production code calls an OS-level API that's impossible to mock with interfaces:

```csharp
public class OrderService
{
    public bool ProcessOrder(Order order)
    {
        // Calls a static method that talks to the OS certificate store — can't be faked with DI
        bool isValid = CertValidator.VerifyCertInMachine();
        if (!isValid) return false;

        PaymentGateway.Charge(order.Amount);
        return true;
    }
}
```

With InjectorPP.Net, you test both paths by faking the dependencies — without touching the production code:

```csharp
[Fact]
public void ProcessOrder_WhenCertIsValid_ShouldSucceed()
{
    using var injector = new Injector();
    injector.WhenCalled(typeof(CertValidator).GetMethod(nameof(CertValidator.VerifyCertInMachine))!)
            .WillReturn(true);
    injector.WhenCalled(typeof(PaymentGateway).GetMethod(nameof(PaymentGateway.Charge))!)
            .WillDoNothing();

    var service = new OrderService();
    Assert.True(service.ProcessOrder(order));
}

[Fact]
public void ProcessOrder_WhenCertIsInvalid_ShouldFail()
{
    using var injector = new Injector();
    injector.WhenCalled(typeof(CertValidator).GetMethod(nameof(CertValidator.VerifyCertInMachine))!)
            .WillReturn(false);

    var service = new OrderService();
    Assert.False(service.ProcessOrder(order));  // PaymentGateway.Charge is never reached
}
```

**Zero interfaces. Zero DI. Zero changes to production code. Full test coverage.**

## API Reference

### `Injector`

| Method | Description |
|--------|-------------|
| `WhenCalled(MethodInfo)` | Target a method by its `MethodInfo` |
| `WhenCalled(() => Method())` | Target a static method via lambda expression |
| `WhenCalled<T>(string)` | Target a private/protected method by name |
| `WhenCalled(Type, string)` | Target a private/protected method by type and name |
| `Dispose()` | Restore all original method behaviors |

### `InjectionBuilder`

| Method | Description |
|--------|-------------|
| `WillReturn<T>(T value)` | Make the method return a specific value |
| `WillThrow<TException>()` | Make the method throw an exception |
| `WillThrow(Exception)` | Make the method throw a specific exception instance |
| `WillExecute(Delegate)` | Replace the method with a custom delegate |
| `WillExecute(MethodInfo)` | Replace the method with another method |
| `WillDoNothing()` | Make the method return `default` for its type |

## Platform Support

| Platform | Architecture | Status |
|----------|-------------|--------|
| Windows | x64 | ✅ |
| Windows | x86 | ✅ |
| Windows | ARM64 | ✅ |
| Linux | x64 | ✅ |
| Linux | ARM64 | ✅ |

## License

[MIT](LICENSE)
