$ErrorActionPreference = 'Stop'
$image = (Get-Content (Join-Path $PSScriptRoot '..\..\resources\lexicons\pronunciation-replay.v1.json') -Raw |
    ConvertFrom-Json).image
$arguments = @(
    'run', '--rm', '--pull', 'never', '--network', 'none',
    '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges',
    '--pids-limit', '128', '--memory', '1g', '--cpus', '2',
    '--mount', "type=bind,source=$(Join-Path $PSScriptRoot 'smoke'),target=/inputs,readonly",
    '--mount', "type=bind,source=$PSScriptRoot,target=/adapter,readonly",
    '--entrypoint', 'php', $image, '/adapter/replay.php'
)
$first = & docker @arguments
if ($LASTEXITCODE -ne 0) {
    throw 'Pinned MediaWiki smoke execution failed.'
}
$result = $first | ConvertFrom-Json
$good = $result.results[0]
if (($good.readings -join '|') -cne '/pat/|[pat]' -or
    $good.errors.Count -ne 0 -or $good.unsupported.Count -ne 0) {
    throw "Synthetic original IPA/frame/content replay failed: $($good | ConvertTo-Json -Depth 5)"
}
if ($good.requested -notcontains 'Module:smoke/respelling' -or
    $good.requested -notcontains 'Module:smoke/data' -or
    $good.requested -notcontains 'Module:smoke/json' -or $good.requested -notcontains 'test') {
    throw 'Dynamic data/content closure was not traced.'
}
if ($result.results[1].errors.Count -eq 0 -or
    $result.results[1].requested -notcontains 'Module:smoke/missing') {
    throw 'Changed frame did not surface a missing dynamic dependency.'
}
if ($result.results[2].unsupported -notcontains 'mw.title.getCategories') {
    throw "Unsupported runtime input was not explicit: $($result.results[2] | ConvertTo-Json -Depth 5)"
}
if ($result.results[3].errors.Count -eq 0 -or
    $result.results[3].requested -notcontains 'Module:smoke/missing-json') {
    throw 'Missing JSON data was not traced before the MediaWiki existence check.'
}
$second = & docker @arguments
if ($LASTEXITCODE -ne 0) {
    throw 'Repeated pinned MediaWiki smoke execution failed.'
}
if (($first -join "`n") -cne ($second -join "`n")) {
    throw 'Fresh executions were not byte-stable.'
}
Write-Output 'Pinned runtime smoke passed: alternatives, frame/content, dynamic require/loadData, explicit blockers, byte stability.'
