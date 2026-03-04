$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$publishDir = Join-Path $repoRoot 'src\Paste.App\bin\Release\net8.0-windows\win-x64\publish'
$outputPath = Join-Path $PSScriptRoot 'AppFiles.wxs'

if (-not (Test-Path $publishDir)) {
    throw "Publish directory not found: $publishDir"
}

$files = Get-ChildItem -Path $publishDir -File | Sort-Object Name

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
$lines.Add('  <Fragment>')
$lines.Add('    <ComponentGroup Id="AppFiles" Directory="INSTALLFOLDER">')

$index = 1
foreach ($file in $files) {
    $id = ('AppFile{0:D4}' -f $index)
    $src = $file.FullName.Replace('&', '&amp;')
    $lines.Add("      <Component Id=""$id"" Guid=""*"">")
    $lines.Add("        <File Source=""$src"" />")
    $lines.Add('      </Component>')
    $index++
}

$lines.Add('    </ComponentGroup>')
$lines.Add('  </Fragment>')
$lines.Add('</Wix>')

Set-Content -Path $outputPath -Value $lines -Encoding UTF8
Write-Host "Generated $outputPath with $($files.Count) files."
