$lines = Get-Content 'C:\Users\win\.gemini\antigravity-ide\brain\bffe9143-d85b-4e1c-8df3-5fae7d28a7cf\.system_generated\logs\transcript_full.jsonl' -Encoding utf8
foreach ($line in $lines) {
    if ($line -match '"step_index":285') {
        Write-Output "=== STEP 285 ==="
        Write-Output $line
    }
}
