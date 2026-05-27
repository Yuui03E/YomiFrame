$files = Get-ChildItem -Path . -Recurse -Include *.cs,*.xaml,*.csproj,*.sln,README.md | Where-Object { $_.FullName -notmatch '\\obj\\' -and $_.FullName -notmatch '\\bin\\' -and $_.FullName -notmatch '\\.git\\' }

foreach ($file in $files) {
    $content = Get-Content -Path $file.FullName -Raw
    if ($content -match 'KuroReader') {
        $content = $content -replace 'KuroReader', 'YomiFrame'
        Set-Content -Path $file.FullName -Value $content -NoNewline
    }
}

Rename-Item -Path "src\KuroReader\KuroReader.csproj" -NewName "YomiFrame.csproj"
Rename-Item -Path "KuroReader.sln" -NewName "YomiFrame.sln"
Rename-Item -Path "src\KuroReader" -NewName "YomiFrame"
