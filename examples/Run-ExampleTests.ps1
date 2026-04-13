$ErrorActionPreference = "Stop"

Push-Location $PSScriptRoot
try {
    $env:DOTNET_ReadyToRun = "0"
    $env:DOTNET_TieredCompilation = "0"
    $env:DOTNET_JitNoInline = "1"

    $exampleTestProjects = Get-ChildItem -Path . -Recurse -Filter *.csproj |
        Where-Object { $_.Name -like "*.Tests.csproj" } |
        Sort-Object FullName

    if ($exampleTestProjects.Count -eq 0) {
        Write-Host "No example test projects found under examples."
        exit 0
    }

    foreach ($project in $exampleTestProjects) {
        Write-Host "Running example test project: $($project.FullName)"

        dotnet test $project.FullName `
            --configuration Release `
            --logger "trx;LogFileName=test-results.trx" `
            -p:InjectorPPTestMode=true `
            -- RunConfiguration.TreatNoTestsAsError=true

        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
}
finally {
    Pop-Location
}
