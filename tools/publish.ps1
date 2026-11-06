# Publie l'application dans dist\ : un exécutable autonome (runtime .NET inclus), prêt à copier et lancer.
#   pwsh tools/publish.ps1           → dist\TaskbarWidgets.exe + plugin d'exemple dans dist\Widgets
#   pwsh tools/publish.ps1 -NoSample → sans le plugin d'exemple
param(
    [string]$Runtime = "win-x64",
    [switch]$NoSample
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$dist = Join-Path $root "dist"

function Get-DistProcesses {
    # Win32_Process donne le chemin même quand Get-Process ne peut pas le lire.
    Get-CimInstance Win32_Process |
        Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith("$dist\", [StringComparison]::OrdinalIgnoreCase) }
}

function Clear-Dist {
    if (-not (Test-Path $dist)) { return }

    # 1. Arrêter les programmes lancés depuis dist (l'exe ne peut pas être supprimé pendant qu'il tourne).
    foreach ($p in Get-DistProcesses) {
        Write-Host "Arrêt de $($p.Name) (PID $($p.ProcessId)) lancé depuis dist…"
        Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $p.ProcessId -Timeout 10 -ErrorAction SilentlyContinue
    }

    # 2. Supprimer en réessayant : après l'arrêt d'un processus, Windows (ou l'antivirus qui analyse
    #    l'exe fraîchement écrit) peut garder le fichier verrouillé quelques instants.
    $deadline = (Get-Date).AddSeconds(20)
    while ($true) {
        try {
            Remove-Item $dist -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            if ((Get-Date) -ge $deadline) {
                $holders = Get-DistProcesses | ForEach-Object { "$($_.Name) (PID $($_.ProcessId))" }
                $detail = if ($holders) { "Toujours en cours d'exécution : $($holders -join ', ')." }
                          else { "Un programme garde un fichier ouvert (Explorateur avec aperçu, antivirus, invite de commandes placée dans dist…)." }
                throw "Impossible de vider dist : $($_.Exception.Message)`n$detail Fermez-le puis relancez le script."
            }
            Start-Sleep -Milliseconds 500
        }
    }
}

Clear-Dist

dotnet publish (Join-Path $root "src\TaskbarWidgets\TaskbarWidgets.csproj") `
    -c Release -r $Runtime --self-contained true -o $dist `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw "dotnet publish a échoué." }
Remove-Item (Join-Path $dist "*.xml") -ErrorAction SilentlyContinue # documentation du SDK, inutile à l'exécution

if (-not $NoSample) {
    $sample = Join-Path $root "samples\TaskbarWidgets.Sample.Uptime\TaskbarWidgets.Sample.Uptime.csproj"
    dotnet build $sample -c Release -p:DebugType=none -o (Join-Path $root "samples\TaskbarWidgets.Sample.Uptime\bin\dist")
    if ($LASTEXITCODE -ne 0) { throw "Compilation du plugin d'exemple échouée." }
    New-Item -ItemType Directory -Force (Join-Path $dist "Widgets") | Out-Null
    Copy-Item (Join-Path $root "samples\TaskbarWidgets.Sample.Uptime\bin\dist\TaskbarWidgets.Sample.Uptime.dll") (Join-Path $dist "Widgets")
}

Copy-Item (Join-Path $root "README.md") $dist
Write-Host "`ndist prêt :" -ForegroundColor Green
Get-ChildItem $dist -Recurse -File | ForEach-Object { "{0,-45} {1,8:N1} Mo" -f $_.FullName.Substring($dist.Length + 1), ($_.Length / 1MB) }
