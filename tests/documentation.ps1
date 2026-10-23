$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$schema = Get-Content -LiteralPath (Join-Path $project 'docs\documentation-schema.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($schema.schemaVersion -ne 1) { throw 'Unsupported documentation schema version.' }
$utf8 = New-Object System.Text.UTF8Encoding($false, $true)
$registered = @{}
$failures = New-Object 'System.Collections.Generic.List[string]'
foreach ($document in $schema.documents) {
    $relative = [string]$document.path
    if ($relative -cnotmatch '^(?:[A-Z][A-Z0-9_]*\.md|docs/[A-Z][A-Z0-9_]*\.md)$') { $failures.Add("Invalid canonical document path: $relative"); continue }
    if ($registered.ContainsKey($relative.ToLowerInvariant())) { $failures.Add("Duplicate documentation registration: $relative"); continue }
    $registered[$relative.ToLowerInvariant()] = $relative
    $path = Join-Path $project $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { $failures.Add("Missing document: $relative"); continue }
    # Validate actual filename case component-by-component even on Windows.
    $parent = $project
    foreach ($part in ($relative -split '/')) {
        $match = @(Get-ChildItem -LiteralPath $parent -Force | Where-Object { $_.Name -ceq $part })
        if ($match.Count -ne 1) { $failures.Add("Incorrect path casing: $relative"); break }
        $parent = $match[0].FullName
    }
    try { $text = $utf8.GetString([IO.File]::ReadAllBytes($path)).TrimStart([char]0xFEFF) }
    catch { $failures.Add("Document is not valid UTF-8: $relative"); continue }
    $visible = [regex]::Replace($text, '(?ms)^```[^\r\n]*\r?\n.*?^```[^\r\n]*$', '')
    $titles = @([regex]::Matches($visible, '(?m)^# ([^\r\n]+)\r?$') | ForEach-Object { $_.Groups[1].Value.Trim() })
    if ($titles.Count -ne 1 -or $titles[0] -cne $document.title) { $failures.Add("Incorrect H1 title: $relative") }
    $sections = @([regex]::Matches($visible, '(?m)^## ([^\r\n]+)\r?$') | ForEach-Object { $_.Groups[1].Value.Trim() })
    if ($document.allowVersionSections) {
        if ($sections.Count -lt 1 -or $sections[0] -cne '[Unreleased]') { $failures.Add("Changelog must start with [Unreleased]: $relative") }
        for ($i = 1; $i -lt $sections.Count; $i++) {
            if ($sections[$i] -notmatch '^\[\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?\] - \d{4}-\d{2}-\d{2}$') { $failures.Add("Invalid version heading: $($sections[$i])") }
        }
        $versionBodies = @([regex]::Matches($visible, '(?ms)^## [^\r\n]+\r?\n(.*?)(?=^## |\z)'))
        foreach ($body in $versionBodies) {
            $subsections = @([regex]::Matches($body.Groups[1].Value, '(?m)^### ([^\r\n]+)\r?$') | ForEach-Object { $_.Groups[1].Value.Trim() })
            if (($subsections -join '|') -cne (@($document.subsectionsPerVersion) -join '|')) { $failures.Add("Incorrect changelog subsection order: $relative") }
        }
    } elseif (($sections -join '|') -cne (@($document.sections) -join '|')) { $failures.Add("Incorrect H2 section order: $relative") }
    foreach ($link in [regex]::Matches($visible, '\]\(([^)]+)\)')) {
        $target = $link.Groups[1].Value.Trim()
        if ($target -match '^(?:[a-zA-Z][a-zA-Z0-9+.-]*:|#|/)') { continue }
        $target = [Uri]::UnescapeDataString(($target -split '#', 2)[0])
        if ($target -and -not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $path) $target))) { $failures.Add("Broken local link in ${relative}: $target") }
    }
}
$actual = @(Get-ChildItem -LiteralPath $project -File -Filter '*.md') + @(Get-ChildItem -LiteralPath (Join-Path $project 'docs') -File -Filter '*.md' -Recurse)
foreach ($file in $actual) {
    $relative = $file.FullName.Substring($project.Length + 1).Replace([IO.Path]::DirectorySeparatorChar, [char]'/')
    if (-not $registered.ContainsKey($relative.ToLowerInvariant())) { $failures.Add("Unregistered document: $relative") }
}
if ($failures.Count -gt 0) { throw ($failures -join [Environment]::NewLine) }
Write-Output "PASS: $($schema.documents.Count) canonical documents, exact paths, heading order, UTF-8 and local links."
