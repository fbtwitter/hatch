param([string]$LottieGenPath = 'LottieGen')

$ErrorActionPreference = 'Stop'
$animationRoot = Join-Path $PSScriptRoot '..\Animations'
$generationFolder = Join-Path ([System.IO.Path]::GetTempPath()) ('hatch-mascot-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $generationFolder | Out-Null
& $LottieGenPath -InputFile (Join-Path $animationRoot 'Source\*.json') -Language cs -WinUIVersion 3.0 -Namespace Hatch.Animations -OutputFolder $generationFolder
if ($LASTEXITCODE -ne 0) { throw 'LottieGen failed to generate the mascot sources.' }
Get-ChildItem -LiteralPath $generationFolder -Filter '*.cs' | ForEach-Object {
    $sourceText = [System.IO.File]::ReadAllText($_.FullName)
    $sourceText = $sourceText.Replace('sealed class ', 'sealed partial class ')
    $sourceText = [regex]::Replace($sourceText, '[ \t]+(?=\r?$)', '', [System.Text.RegularExpressions.RegexOptions]::Multiline)
    $destination = Join-Path $animationRoot ('Generated\' + $_.BaseName + '.g.cs')
    [System.IO.File]::WriteAllText($destination, $sourceText, [System.Text.UTF8Encoding]::new($false))
}
Write-Output 'Mascot sources regenerated with Native AOT partial classes.'
