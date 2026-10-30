$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[xml]$props = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../../Directory.Build.props') -Raw
$version = [string]$props.Project.PropertyGroup.Version
if ($env:GITHUB_REF_NAME -like 'release/*' -and $env:GITHUB_REF_NAME -ne "release/$version") { throw 'Release branch/version mismatch' }
$ids = @('LlmChamber', 'LlmChamber.Wpf', 'LlmChamber.Avalonia', 'LlmChamber.WinForms')
$files = @(Get-ChildItem -LiteralPath artifacts -Filter '*.nupkg' -File)
if ($files.Count -ne $ids.Count) { throw 'Unexpected package count' }
foreach ($id in $ids) {
    $path = Join-Path (Resolve-Path artifacts).Path "$id.$version.nupkg"
    $zip = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        $spec = @($zip.Entries | Where-Object FullName -Like '*.nuspec')
        if ($spec.Count -ne 1) { throw 'Missing package metadata' }
        $reader = [IO.StreamReader]::new($spec[0].Open())
        try { [xml]$xml = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($xml.package.metadata.id -ne $id -or $xml.package.metadata.version -ne $version) { throw 'Package identity mismatch' }
        $tfms = if ($id -in @('LlmChamber.Wpf','LlmChamber.WinForms')) { @('net8.0-windows7.0','net10.0-windows7.0') } else { @('net8.0','net10.0') }
        foreach ($tfm in $tfms) {
            if (-not $zip.GetEntry("lib/$tfm/$id.dll")) { throw "Missing $id/$tfm assembly" }
        }
        foreach ($required in @('README.md','icon.png')) { if (-not $zip.GetEntry($required)) { throw "Missing $required" } }
        $records = foreach ($entry in ($zip.Entries | Where-Object { $_.FullName -ne '.signature.p7s' } | Sort-Object FullName -CaseSensitive)) {
            $stream = $entry.Open()
            try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
            "$($entry.FullName):$hash"
        }
        $payload = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($records -join "`n"))))
        Write-Output "VERIFIED_PAYLOAD $id $version $payload"
        Write-Output "VERIFIED_PACKAGE $id $((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash)"
    } finally { $zip.Dispose() }
}
