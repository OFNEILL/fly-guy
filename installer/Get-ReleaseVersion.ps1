# Run after checkout with full tag history. Beta tags do not affect production versions.
$ErrorActionPreference = 'Stop'
$tags = @(git tag --list 'v*')
if ($LASTEXITCODE -ne 0) { throw 'Could not read release tags.' }
$versions = @($tags | Where-Object { $_ -match '^v\d+\.\d+\.\d+$' } | ForEach-Object { [version]$_.Substring(1) } | Sort-Object -Descending)
if ($versions.Count -eq 0) { '1.0.0'; return }
$latest = $versions[0]
$releaseCommit = git rev-list -n 1 "v$latest"
if ($LASTEXITCODE -ne 0) { throw 'Could not resolve release tag.' }
$current = git rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Could not resolve current commit.' }
if ($releaseCommit -eq $current) { $latest.ToString(); return }
# A delayed older run must not become a newer production release.
git merge-base --is-ancestor $current $releaseCommit
if ($LASTEXITCODE -eq 0) { return }
if ($LASTEXITCODE -ne 1) { throw 'Could not compare release history.' }
'{0}.{1}.{2}' -f $latest.Major, $latest.Minor, ($latest.Build + 1)
