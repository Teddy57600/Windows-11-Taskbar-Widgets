# Publie l'application dans dist\ : un exécutable autonome (runtime .NET inclus), prêt à copier et lancer.
#   pwsh tools/publish.ps1           → dist\TaskbarWidgets.exe + plugins (plugins\ et exemple) dans dist\Widgets
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

# Compiler d'abord : une erreur de compilation ne doit pas laisser dist\ vide.
dotnet build (Join-Path $root "TaskbarWidgets.slnx") -c Release -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw "Compilation échouée : dist\ n'a pas été modifié." }

Clear-Dist

dotnet publish (Join-Path $root "src\TaskbarWidgets\TaskbarWidgets.csproj") `
    -c Release -r $Runtime --self-contained true -o $dist `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw "dotnet publish a échoué." }
Remove-Item (Join-Path $dist "*.xml") -ErrorAction SilentlyContinue # documentation du SDK, inutile à l'exécution

# Plugins livrés : tous les projets de plugins\, plus l'exemple de samples\ (sauf -NoSample).
$plugins = @(Get-ChildItem (Join-Path $root "plugins") -Filter *.csproj -Recurse -ErrorAction SilentlyContinue)
if (-not $NoSample) { $plugins += Get-Item (Join-Path $root "samples\TaskbarWidgets.Sample.Uptime\TaskbarWidgets.Sample.Uptime.csproj") }
foreach ($plugin in $plugins) {
    $out = Join-Path $plugin.DirectoryName "bin\dist"
    dotnet build $plugin.FullName -c Release -p:DebugType=none -o $out
    if ($LASTEXITCODE -ne 0) { throw "Compilation du plugin $($plugin.BaseName) échouée." }
    New-Item -ItemType Directory -Force (Join-Path $dist "Widgets") | Out-Null
    Copy-Item (Join-Path $out "$($plugin.BaseName).dll") (Join-Path $dist "Widgets")
}

Copy-Item (Join-Path $root "README.md"), (Join-Path $root "LICENSE") $dist
Write-Host "`ndist prêt :" -ForegroundColor Green
Get-ChildItem $dist -Recurse -File | ForEach-Object { "{0,-45} {1,8:N1} Mo" -f $_.FullName.Substring($dist.Length + 1), ($_.Length / 1MB) }
