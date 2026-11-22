# Compila DXAllert, esegue i test e genera dist\DXAllert<versione>.exe (versione da DXAllert.App.csproj)
# Uso (da PowerShell, nella cartella del progetto):   .\build.ps1
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if ($PSScriptRoot.Length -gt 120) {
    Write-Warning 'Il percorso della cartella del progetto è molto lungo e Inno Setup potrebbe non trovare i file. Se il passo 3 fallisce, sposta il progetto in una cartella corta, per esempio C:\DXAllert.'
}

Write-Host '1/3  Test automatici...' -ForegroundColor Cyan
dotnet test tests\DXAllert.Tests -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Test falliti: installer non generato.' }

Write-Host '2/3  Pubblicazione self-contained (win-x64)...' -ForegroundColor Cyan
if (Test-Path publish) { Remove-Item publish -Recurse -Force }
dotnet publish src\DXAllert.App -c Release -r win-x64 --self-contained true -o publish --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Pubblicazione non riuscita.' }

Write-Host '3/3  Creazione installer con Inno Setup...' -ForegroundColor Cyan
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 non trovato. Installalo con:  winget install JRSoftware.InnoSetup' }
$version = ([xml](Get-Content src\DXAllert.App\DXAllert.App.csproj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
& $iscc /Q "/DAppVersion=$version" installer\DXAllert.iss
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup ha segnalato un errore.' }

$setup = Get-Item "dist\DXAllert$version.exe"
Write-Host ("Fatto: {0} ({1:N1} MB)" -f $setup.FullName, ($setup.Length / 1MB)) -ForegroundColor Green
