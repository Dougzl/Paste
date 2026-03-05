param(
    [string]$SourceDir = ""
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$sourcePath = if ([string]::IsNullOrWhiteSpace($SourceDir)) {
    Join-Path $repoRoot 'src\Paste.App\bin\Release\net8.0-windows\win-x64\publish'
}
else {
    (Resolve-Path $SourceDir).Path
}
$outputPath = Join-Path $PSScriptRoot 'AppFiles.wxs'

if (-not (Test-Path $sourcePath)) {
    throw "Source directory not found: $sourcePath"
}

$files = Get-ChildItem -Path $sourcePath -Recurse -File | Sort-Object FullName

if ($files.Count -eq 0) {
    throw "No files found under source directory: $sourcePath"
}

function Get-RelativeDirectoryPath {
    param(
        [string]$BasePath,
        [string]$DirectoryPath
    )

    $normalizedBase = [System.IO.Path]::GetFullPath($BasePath).TrimEnd('\')
    $normalizedDir = [System.IO.Path]::GetFullPath($DirectoryPath).TrimEnd('\')

    if ($normalizedDir -eq $normalizedBase) {
        return ''
    }

    if (-not $normalizedDir.StartsWith($normalizedBase + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path '$normalizedDir' is not under '$normalizedBase'."
    }

    return $normalizedDir.Substring($normalizedBase.Length + 1)
}

$dirIdMap = New-Object 'System.Collections.Generic.Dictionary[string,string]'
$dirIdMap[''] = 'INSTALLFOLDER'

$relativeDirs = New-Object System.Collections.Generic.HashSet[string]
foreach ($file in $files) {
    $relativeDir = Get-RelativeDirectoryPath -BasePath $sourcePath -DirectoryPath $file.DirectoryName
    if (-not [string]::IsNullOrEmpty($relativeDir)) {
        [void]$relativeDirs.Add($relativeDir)
    }
}

$orderedDirs = $relativeDirs `
    | Sort-Object { ($_ -split '[\\/]').Count }, { $_.ToLowerInvariant() }

$dirIndex = 1
foreach ($relativeDir in $orderedDirs) {
    $dirId = 'AppDir{0:D4}' -f $dirIndex
    $dirIdMap[$relativeDir] = $dirId
    $dirIndex++
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
$lines.Add('  <Fragment>')

foreach ($relativeDir in $orderedDirs) {
    $parentRelativeDir = [System.IO.Path]::GetDirectoryName($relativeDir)
    if ([string]::IsNullOrEmpty($parentRelativeDir)) {
        $parentRelativeDir = ''
    }
    $parentId = $dirIdMap[$parentRelativeDir]
    $dirId = $dirIdMap[$relativeDir]
    $leafName = [System.IO.Path]::GetFileName($relativeDir).Replace('&', '&amp;')
    $lines.Add("    <DirectoryRef Id=""$parentId"">")
    $lines.Add("      <Directory Id=""$dirId"" Name=""$leafName"" />")
    $lines.Add('    </DirectoryRef>')
}

$lines.Add('  </Fragment>')
$lines.Add('  <Fragment>')
$lines.Add('    <ComponentGroup Id="AppFiles" Directory="INSTALLFOLDER">')

$index = 1
foreach ($file in $files) {
    $relativeDir = Get-RelativeDirectoryPath -BasePath $sourcePath -DirectoryPath $file.DirectoryName
    $componentDirectory = $dirIdMap[$relativeDir]
    $id = ('AppFile{0:D4}' -f $index)
    $src = $file.FullName.Replace('&', '&amp;')
    $lines.Add("      <Component Id=""$id"" Guid=""*"" Directory=""$componentDirectory"">")
    $lines.Add("        <File Source=""$src"" />")
    $lines.Add('      </Component>')
    $index++
}

$lines.Add('    </ComponentGroup>')
$lines.Add('  </Fragment>')
$lines.Add('</Wix>')

Set-Content -Path $outputPath -Value $lines -Encoding UTF8
Write-Host "Generated $outputPath with $($files.Count) files."
