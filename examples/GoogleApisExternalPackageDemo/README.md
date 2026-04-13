# Google APIs External Package Demo

This example shows how to use **InjectorPP.Net** to fake a method from an **external package** instead of from your own codebase.

The scenario is a realistic **Google Workspace sign-in flow**:

- production code receives a Google ID token from the application
- production code calls `GoogleJsonWebSignature.ValidateAsync(...)`
- production code only signs the user in when the email is verified and the hosted domain matches the company domain

The example uses `Google.Apis.Auth` and fakes:

```csharp
GoogleJsonWebSignature.ValidateAsync(...)
```

## Structure

- `Demo.GoogleProduct` contains a `GoogleWorkspaceSignInService` that validates Google ID tokens for a company domain.
- `Demo.GoogleProduct.Tests` contains InjectorPP.Net tests that replace the Google API method at runtime.

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
await GoogleJsonWebSignature.ValidateAsync(
    idToken,
    new GoogleJsonWebSignature.ValidationSettings
    {
        HostedDomain = _expectedHostedDomain
    })
```

The tests intercept that external-package method and return fake Google payloads instead:

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
        Email = "ada@contoso.com",
        EmailVerified = true,
        Name = "Ada Lovelace",
        HostedDomain = "contoso.com"
    }));
```
