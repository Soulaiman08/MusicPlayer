$lines = Get-Content 'C:\Users\win\.gemini\antigravity-ide\brain\bffe9143-d85b-4e1c-8df3-5fae7d28a7cf\.system_generated\logs\transcript_full.jsonl' -Encoding utf8
foreach ($line in $lines) {
    if ($line -match '"type":"USER_INPUT"') {
        $obj = $line | ConvertFrom-Json
        if ($obj.content -match 'START REPAIR PHASE') {
            [System.IO.File]::WriteAllText("c:\Users\win\Desktop\Soulaiman\tools\user_repair_request.txt", $obj.content, [System.Text.Encoding]::UTF8)
            Write-Output "Saved to user_repair_request.txt, length: $($obj.content.Length)"
        }
    }
}
