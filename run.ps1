# Build and Run script for MusicPlayer
# Bypasses IDE file-lock on obj\ folder by using temp intermediate path

$env:DOTNET_ROOT = "C:\Program Files\dotnet"
$project = "c:\Users\win\Desktop\Soulaiman\src\MusicPlayer\MusicPlayer.csproj"
$tmpObj  = "C:\Users\win\AppData\Local\Temp\MusicPlayerBuild"
$outDir  = "c:\Users\win\Desktop\Soulaiman\src\MusicPlayer\bin\RunFinal"

if (!(Test-Path $tmpObj)) { New-Item -ItemType Directory -Path $tmpObj | Out-Null }

Write-Host "==> Building..." -ForegroundColor Cyan
& "C:\Program Files\dotnet\dotnet.exe" build $project `
    /p:AssemblyName=MusicPlayerFinal `
    /p:OutputPath=$outDir\ `
    /p:IntermediateOutputPath=$tmpObj\ `
    2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "==> Build succeeded. Launching..." -ForegroundColor Green
    Start-Process "C:\Program Files\dotnet\dotnet.exe" `
        -ArgumentList "`"$outDir\MusicPlayerFinal.dll`"" `
        -WorkingDirectory $outDir
} else {
    Write-Host "==> Build FAILED." -ForegroundColor Red
}
