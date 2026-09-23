param([string]$UnityEditorPath)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $UnityEditorPath) {
    $versionLine = Get-Content -LiteralPath (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') | Select-Object -First 1
    $version = ($versionLine -split ':', 2)[1].Trim()
    $UnityEditorPath = "C:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe"
}
if (-not (Test-Path -LiteralPath $UnityEditorPath)) { throw "Unity Editor not found: $UnityEditorPath" }
$validationRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('SplineJsonValidation-' + [guid]::NewGuid().ToString('N'))
foreach ($relative in @('Assets/Scripts', 'Assets/Editor', 'Assets/Resources/data', 'Packages', 'ProjectSettings')) {
    New-Item -ItemType Directory -Path (Join-Path $validationRoot $relative) -Force | Out-Null
}
foreach ($name in @('SplineJson.cs', 'SplineJsonData.cs', 'SplineJsonMeshBuilder.cs')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot "Assets/Scripts/routes/$name") -Destination (Join-Path $validationRoot 'Assets/Scripts')
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'Assets/Editor/SplineJsonEditor.cs') -Destination (Join-Path $validationRoot 'Assets/Editor')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SplineJsonValidation.cs') -Destination (Join-Path $validationRoot 'Assets/Editor')
Copy-Item -LiteralPath (Join-Path $projectRoot 'Assets/Resources/data/junction.json') -Destination (Join-Path $validationRoot 'Assets/Resources/data')
Copy-Item -LiteralPath (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $validationRoot 'ProjectSettings')
$dependencies = @{}
foreach ($package in @('com.unity.splines', 'com.unity.mathematics', 'com.unity.settings-manager')) {
    $cachedPackage = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Library/PackageCache') -Directory -Filter "$package@*" | Select-Object -First 1
    if (-not $cachedPackage) { throw "Missing cached package: $package. Open the main project in Unity first." }
    $dependencies[$package] = 'file:' + $cachedPackage.FullName.Replace('\', '/')
}
foreach ($module in @('imgui', 'physics', 'jsonserialize', 'imageconversion')) {
    $dependencies["com.unity.modules.$module"] = '1.0.0'
}
$manifest = @{ dependencies = $dependencies } | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText((Join-Path $validationRoot 'Packages/manifest.json'), $manifest, [System.Text.UTF8Encoding]::new($false))
$logPath = Join-Path $validationRoot 'validation.log'
$arguments = @('-batchmode', '-projectPath', ('"' + $validationRoot + '"'), '-executeMethod', 'SplineJsonValidation.Run', '-logFile', ('"' + $logPath + '"'))
Write-Output "Validation project: $validationRoot"
$process = Start-Process -FilePath $UnityEditorPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
$resultPath = Join-Path $validationRoot 'validation-result.txt'
if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath }
if ($process.ExitCode -ne 0) { throw "Unity validation failed. See $logPath" }
if (-not (Test-Path -LiteralPath $resultPath)) { throw "Unity did not produce a validation report. See $logPath" }
Write-Output "Preview: $(Join-Path $validationRoot 'mesh-preview.png')"
