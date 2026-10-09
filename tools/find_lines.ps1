$lines = Get-Content 'src\MusicPlayer\Views\MainWindow.xaml'
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match 'ShowEqualizer') { Write-Output "ShowEqualizer: $($i+1)" }
    if ($lines[$i] -match 'ShowSoundEffects') { Write-Output "ShowSoundEffects: $($i+1)" }
    if ($lines[$i] -match 'QueueDrawerOverlay') { Write-Output "QueueDrawerOverlay: $($i+1)" }
}
