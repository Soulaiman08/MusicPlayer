$lines = Get-Content 'C:\Users\win\.gemini\antigravity-ide\brain\bffe9143-d85b-4e1c-8df3-5fae7d28a7cf\.system_generated\logs\transcript_full.jsonl' -Encoding utf8
foreach ($line in $lines) {
    if ($line -match '"type":"USER_INPUT"') {
        Write-Output "=== USER INPUT ==="
        $obj = $line | ConvertFrom-Json
        Write-Output $obj.content
    }
}
