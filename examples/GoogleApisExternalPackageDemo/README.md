# Google APIs External Package Demo

This example shows how to use **InjectorPP.Net** to fake a method from an **external package** instead of from your own codebase.

It uses `Google.Apis.Auth` and fakes:

```csharp
GoogleJsonWebSignature.ValidateAsync(...)
```

## Structure

- `Demo.GoogleProduct` contains production code that directly calls the Google API package.
- `Demo.GoogleProduct.Tests` contains the InjectorPP.Net test that replaces the Google API method at runtime.

## Product Project Setup

The production project uses a conditional MSBuild property group so test runs can build it without optimizations:

```xml
<PropertyGroup Condition="'$(InjectorPPTestMode)' == 'true'">
  <Optimize>false</Optimize>
  <TieredCompilation>false</TieredCompilation>
</PropertyGroup>
```

## Running the Example

Use the included script:

```powershell
.\Run-Tests.ps1
```

That script runs the test process with these JIT-related variables:

```powershell
$env:DOTNET_ReadyToRun = "0"
$env:DOTNET_TieredCompilation = "0"
$env:DOTNET_JitNoInline = "1"
```

and then executes:

```powershell
dotnet test .\Demo.GoogleProduct.Tests\Demo.GoogleProduct.Tests.csproj --configuration Release -p:InjectorPPTestMode=true
```

## What the Test Demonstrates

The production code calls the external package directly:

```csharp
await GoogleJsonWebSignature.ValidateAsync(jwt, new GoogleJsonWebSignature.ValidationSettings())
```

The test intercepts that external-package method and returns a fake payload instead:

```csharp
injector.WhenCalled(
        typeof(GoogleJsonWebSignature).GetMethod(
            nameof(GoogleJsonWebSignature.ValidateAsync),
            new[]
            {
                typeof(string),
                typeof(GoogleJsonWebSignature.ValidationSettings)
            })!)
    .WillReturn(Task.FromResult(new GoogleJsonWebSignature.Payload
    {
        Email = "fake-user@example.com"
    }));
```
