[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string[]]$Runtime = @("win-x64", "win-arm64"),

    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [bool]$SelfContained = $true,

    [switch]$SkipVersionUpdate,

    [switch]$NoZip
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Path $PSScriptRoot -Parent
$projectPath = Join-Path $repoRoot "src\QobuzPresence.App\QobuzPresence.App.csproj"
$artifactsRoot = Join-Path $repoRoot "artifacts"

if (-not (Test-Path -LiteralPath $projectPath))
{
    throw "Project file not found: $projectPath"
}

if ($Version -notmatch '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?<suffix>[-+].+)?$')
{
    throw "Version must look like 0.2.0 or 0.2.0-beta.1"
}

$major = [int]$Matches.major
$minor = [int]$Matches.minor
$patch = [int]$Matches.patch
$numericVersion = "$major.$minor.$patch.0"

if (-not $SkipVersionUpdate)
{
    [xml]$projectXml = Get-Content -LiteralPath $projectPath
    $propertyGroup = $projectXml.Project.PropertyGroup | Select-Object -First 1

    if ($null -eq $propertyGroup)
    {
        throw "No PropertyGroup found in $projectPath"
    }

    function Set-ProjectProperty
    {
        param(
            [xml]$Xml,
            $PropertyGroup,
            [string]$Name,
            [string]$Value
        )

        $node = $PropertyGroup.SelectSingleNode($Name)

        if ($null -eq $node)
        {
            $node = $Xml.CreateElement($Name)
            [void]$PropertyGroup.AppendChild($node)
        }

        $node.InnerText = $Value
    }

    Set-ProjectProperty -Xml $projectXml -PropertyGroup $propertyGroup -Name "Version" -Value $Version
    Set-ProjectProperty -Xml $projectXml -PropertyGroup $propertyGroup -Name "AssemblyVersion" -Value $numericVersion
    Set-ProjectProperty -Xml $projectXml -PropertyGroup $propertyGroup -Name "FileVersion" -Value $numericVersion
    Set-ProjectProperty -Xml $projectXml -PropertyGroup $propertyGroup -Name "InformationalVersion" -Value $Version

    $projectXml.Save($projectPath)
}

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null

$generatedFiles = [System.Collections.Generic.List[string]]::new()
$singleFileArgs = @(
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true"
)

foreach ($currentRuntime in $Runtime)
{
    $artifactBaseName = "QobuzPresence-v$Version-$currentRuntime"
    $publishDir = Join-Path $artifactsRoot $artifactBaseName
    $zipSuffix = if ($SelfContained) { "self-contained" } else { "framework-dependent" }
    $zipPath = Join-Path $artifactsRoot "$artifactBaseName-$zipSuffix.zip"

    if (Test-Path -LiteralPath $publishDir)
    {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }

    if (Test-Path -LiteralPath $zipPath)
    {
        Remove-Item -LiteralPath $zipPath -Force
    }

    $publishArgs = @(
        "publish",
        $projectPath,
        "-c", $Configuration,
        "-r", $currentRuntime,
        "--self-contained", $SelfContained.ToString().ToLowerInvariant(),
        "-o", $publishDir
    ) + $singleFileArgs

    & dotnet @publishArgs

    if ($LASTEXITCODE -ne 0)
    {
        throw "dotnet publish failed for runtime $currentRuntime"
    }

    $generatedFiles.Add($publishDir) | Out-Null

    if (-not $NoZip)
    {
        Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
        $generatedFiles.Add($zipPath) | Out-Null
    }
}

Write-Host ""
Write-Host "Release artifacts generated:"

foreach ($path in $generatedFiles)
{
    Write-Host " - $path"
}
