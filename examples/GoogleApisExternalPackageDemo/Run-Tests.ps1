$ErrorActionPreference = "Stop"

Push-Location $PSScriptRoot
try {
    $env:DOTNET_ReadyToRun = "0"
    $env:DOTNET_TieredCompilation = "0"
    $env:DOTNET_JitNoInline = "1"

    dotnet test .\Demo.GoogleProduct.Tests\Demo.GoogleProduct.Tests.csproj --configuration Release -p:InjectorPPTestMode=true
}
finally {
    Pop-Location
}
