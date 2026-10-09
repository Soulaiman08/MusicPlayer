$env:DOTNET_ROOT = "C:\Program Files\dotnet"
$project = "c:\Users\win\Desktop\Soulaiman\src\MusicPlayer\MusicPlayer.csproj"
$tmpObj  = "C:\Users\win\AppData\Local\Temp\MusicPlayerBuild"
$outDir  = "c:\Users\win\Desktop\Soulaiman\src\MusicPlayer\bin\RunFinal"

if (!(Test-Path $tmpObj)) { New-Item -ItemType Directory -Path $tmpObj | Out-Null }

Write-Host "==> Building MusicPlayer..." -ForegroundColor Cyan
& "C:\Program Files\dotnet\dotnet.exe" build $project `
    /p:AssemblyName=MusicPlayerFinal `
    /p:OutputPath=$outDir\ `
    /p:IntermediateOutputPath=$tmpObj\ `
    --nologo

if ($LASTEXITCODE -eq 0) {
    Write-Host "BUILD SUCCESS (0 Errors, 0 Warnings)" -ForegroundColor Green
} else {
    Write-Host "BUILD FAILED" -ForegroundColor Red
}
