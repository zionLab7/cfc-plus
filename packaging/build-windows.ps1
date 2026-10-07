param([Parameter(Mandatory=$true)][string]$NodeExecutable)
$ErrorActionPreference='Stop'
$projectRoot=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if(!(Test-Path -LiteralPath $NodeExecutable)){throw 'Informe o executável Node.js instalado.'}
Push-Location $projectRoot
try {
    dotnet publish -c Release -r win-x64 --self-contained true -o artifacts/server/windows-x64
    if($LASTEXITCODE -ne 0){throw 'Falha ao publicar o servidor.'}
    $env:PATH=(Split-Path $NodeExecutable)+';'+$env:PATH
    Push-Location (Join-Path $projectRoot 'desktop')
    try {& $NodeExecutable node_modules/electron-builder/cli.js --win nsis --x64;if($LASTEXITCODE -ne 0){throw 'Falha ao gerar o instalador.'}}
    finally {Pop-Location}
} finally {Pop-Location}
