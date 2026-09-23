param([string]$UnityEditorPath, [ValidateSet('Movement', 'Resources', 'Recovery')][string]$ValidationSuite = 'Movement')

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $UnityEditorPath) {
    $line = Get-Content -LiteralPath (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') | Select-Object -First 1
    $version = ($line -split ':', 2)[1].Trim()
    $UnityEditorPath = "C:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe"
}
if (-not (Test-Path -LiteralPath $UnityEditorPath)) { throw "Unity Editor not found: $UnityEditorPath" }
$validationRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('SplineJsonMovement-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $validationRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'Assets'),(Join-Path $projectRoot 'ProjectSettings') -Destination $validationRoot -Recurse
New-Item -ItemType Directory -Path (Join-Path $validationRoot 'Packages'),(Join-Path $validationRoot 'Assets/Editor') -Force | Out-Null
$testClass = switch ($ValidationSuite) {
    'Resources' { 'SplineResourceMeshValidation' }
    'Recovery' { 'SplineResourceMeshRecoveryValidation' }
    default { 'SplineJsonMovementValidation' }
}
$resultName = switch ($ValidationSuite) {
    'Resources' { 'resource-validation-result.txt' }
    'Recovery' { 'resource-recovery-result.txt' }
    default { 'movement-validation-result.txt' }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot ($testClass + '.cs')) -Destination (Join-Path $validationRoot 'Assets/Editor')
$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'Packages/manifest.json') -Raw | ConvertFrom-Json
$dependencies = @{}
foreach ($item in $manifest.dependencies.PSObject.Properties) { $dependencies[$item.Name] = $item.Value }
foreach ($package in (Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Library/PackageCache') -Directory)) {
    $name = ($package.Name -split '@')[0]
    $dependencies[$name] = 'file:' + $package.FullName.Replace('\', '/')
}
$json = @{ dependencies = $dependencies } | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText((Join-Path $validationRoot 'Packages/manifest.json'), $json, [System.Text.UTF8Encoding]::new($false))
$logPath = Join-Path $validationRoot 'movement-validation.log'
$arguments = @('-batchmode', '-projectPath', ('"' + $validationRoot + '"'), '-executeMethod', ($testClass + '.Run'), '-logFile', ('"' + $logPath + '"'))
Write-Output "Isolated test project: $validationRoot"
$process = Start-Process -FilePath $UnityEditorPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
$resultPath = Join-Path $validationRoot $resultName
if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath }
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $resultPath)) { throw "Movement validation failed. See $logPath" }
