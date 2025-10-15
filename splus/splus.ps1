<# 
  splus.ps1 — compile .usp files without opening S+ Editor
  Usage:
    splus -b -files ".\Module.usp" -s 4 -v
#>

[CmdletBinding()]
param(
    [string]$compiler,
    [switch]$where,
    [switch]$b,
    [switch]$rb,

    # expose as -s / -series (unique aliases); internal name avoids conflicts
    [Alias('s', 'series')]
    [string]$SeriesSpec = '4',   # accepts: 3, 4, or "3,4" (we currently use first value)

    [switch]$w,                  # reserved for future
    [Alias('f')]
    [string[]]$files,
    [switch]$v                   # verbose: echo the exact command
)

function Find-SPlusCC {
    param([string]$Explicit)
    if ($Explicit) {
        $full = Resolve-Path -LiteralPath $Explicit -ErrorAction SilentlyContinue
        if ($full -and (Test-Path $full -PathType Leaf)) { return $full.Path }
    }
    $candidates = @(
        "$Env:ProgramFiles(x86)\Crestron\Simpl\SPlusCC.exe",
        "$Env:ProgramFiles\Crestron\Simpl\SPlusCC.exe"
    )
    foreach ($p in $candidates) { if (Test-Path $p -PathType Leaf) { return (Resolve-Path $p).Path } }
    $onPath = Get-Command SPlusCC -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    return $null
}

function Get-SeriesToken([string]$raw) {
    if ([string]::IsNullOrWhiteSpace($raw)) { return 'series4' }

    # force array, remove empties and whitespace
    $parts = @($raw -split '\s*,\s*') | Where-Object { $_ -and $_.Trim().Length -gt 0 }

    if (-not $parts -or $parts.Count -eq 0) { return 'series4' }

    # take the FIRST token only (keep baseline simple)
    $first = [string]$parts[0]
    $first = $first.Trim().ToLower()

    switch ($first) {
        '3' { return 'series3' }
        '4' { return 'series4' }
        'series3' { return 'series3' }
        'series4' { return 'series4' }
        default { throw "Invalid series '$first'. Use 3 or 4." }
    }
}

$resolved = Find-SPlusCC -Explicit $compiler

if ($where) {
    if ($resolved) { Write-Host $resolved; exit 0 }
    Write-Error "SPlusCC.exe not found. Provide -compiler <path> or install Crestron SIMPL+."
    exit 1
}

if (-not $resolved) {
    Write-Error "SPlusCC.exe not found. Try: splus -where  or  splus -compiler 'C:\...\SPlusCC.exe' -where"
    exit 1
}

if ($b -or $rb) {
    $mode = if ($rb) { '\rebuild' } else { '\build' }
    $seriesTok = Get-SeriesToken $SeriesSpec
    $targetArgs = @('\target', $seriesTok)

    foreach ($f in $files) {
        if ([IO.Path]::GetExtension($f) -ieq '.ush') {
            Write-Warning "Skipping header '$f'. Compile the .usp source instead."
            continue
        }
        $rp = Resolve-Path -LiteralPath $f -ErrorAction SilentlyContinue
        if (-not $rp) { Write-Error "Source not found: $f"; exit 106 }
        $full = $rp.Path

        # argv: \build "<full.usp>" \target seriesN
        $argv = @($mode, "`"$full`"") + $targetArgs
        if ($v) { Write-Host ">> $resolved $($argv -join ' ')" }

        # simple, synchronous execution + capture
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $resolved
        $psi.Arguments = ($argv -join ' ')
        $psi.WorkingDirectory = (Split-Path $full -Parent)
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true

        $p = New-Object System.Diagnostics.Process
        $p.StartInfo = $psi
        [void]$p.Start()
        $stdout = $p.StandardOutput.ReadToEnd()
        $stderr = $p.StandardError.ReadToEnd()
        $p.WaitForExit()
        for ($i = 1; $i -le 100; $i++ ) {
            Write-Progress -Activity "Compiling" -Status "$i% Complete:" -PercentComplete $i
            Start-Sleep -Milliseconds 100 
        }

        if ($stdout) { Write-Host  $stdout.TrimEnd() }
        if ($stderr) { Write-Host  $stderr.TrimEnd() -ForegroundColor DarkCyan }

        if ($p.ExitCode -ne 0) {
            Write-Error "SPlusCC exited $($p.ExitCode) for: $full"
            exit $p.ExitCode
        }
    }
    exit 0
}

# default banner if no action chosen
Write-Host "splus: compiler detected at:`n $resolved"
exit 0
