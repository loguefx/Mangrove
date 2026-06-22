$ErrorActionPreference = "Stop"
$base = $env:MANGROVE_E2E_BASE; if (-not $base) { $base = "http://localhost:5090" }
$samples = Join-Path $env:TEMP "mangrove-samples"

function Hdr($t) { @{ Authorization = "Bearer $t" } }
function Basic($u, $p) { @{ Authorization = "Basic " + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("${u}:${p}")) } }

# 1. Admin (register-first or login)
try {
  $reg = Invoke-RestMethod -Uri "$base/api/auth/register-first" -Method Post -ContentType "application/json" `
    -Body (@{ username = "admin"; email = "a@b.c"; password = "Password123!" } | ConvertTo-Json)
  Write-Host "Registered admin."
}
catch {
  $reg = Invoke-RestMethod -Uri "$base/api/auth/login" -Method Post -ContentType "application/json" `
    -Body (@{ username = "admin"; password = "Password123!" } | ConvertTo-Json)
  Write-Host "Logged in admin."
}
$tok = $reg.accessToken

# 2. Two libraries over the same samples (lib2 stays admin-only)
$lib1 = Invoke-RestMethod -Uri "$base/api/libraries" -Method Post -Headers (Hdr $tok) -ContentType "application/json" `
  -Body (@{ name = "Granted"; type = 0; storageKind = 0; rootPath = $samples; credentialId = $null; folderWatch = $false } | ConvertTo-Json)
$lib2 = Invoke-RestMethod -Uri "$base/api/libraries" -Method Post -Headers (Hdr $tok) -ContentType "application/json" `
  -Body (@{ name = "Restricted"; type = 0; storageKind = 0; rootPath = $samples; credentialId = $null; folderWatch = $false } | ConvertTo-Json)
Invoke-RestMethod -Uri "$base/api/libraries/$($lib1.id)/scan" -Method Post -Headers (Hdr $tok) | Out-Null
Invoke-RestMethod -Uri "$base/api/libraries/$($lib2.id)/scan" -Method Post -Headers (Hdr $tok) | Out-Null
$series = Invoke-RestMethod -Uri "$base/api/libraries/$($lib1.id)/series" -Headers (Hdr $tok)
$s0 = $series[0]
$detail = Invoke-RestMethod -Uri "$base/api/series/$($s0.id)" -Headers (Hdr $tok)
$chapterId = $detail.volumes[0].chapters[0].id
Write-Host "Libraries created (lib1=$($lib1.id), lib2=$($lib2.id)); $($series.Count) series; sample chapter=$chapterId"

# 3. Create a normal user (or reuse), grant only lib1
try {
  $u = Invoke-RestMethod -Uri "$base/api/users" -Method Post -Headers (Hdr $tok) -ContentType "application/json" `
    -Body (@{ username = "reader"; email = $null; password = "Reader123!"; roles = @("User") } | ConvertTo-Json)
}
catch {
  $allUsers = Invoke-RestMethod -Uri "$base/api/users" -Headers (Hdr $tok)
  $u = $allUsers | Where-Object { $_.username -eq "reader" } | Select-Object -First 1
  Invoke-RestMethod -Uri "$base/api/users/$($u.id)/reset-password" -Method Post -Headers (Hdr $tok) -ContentType "application/json" `
    -Body (@{ password = "Reader123!" } | ConvertTo-Json) | Out-Null
}
Invoke-RestMethod -Uri "$base/api/users/$($u.id)" -Method Put -Headers (Hdr $tok) -ContentType "application/json" `
  -Body (@{ libraryIds = @($lib1.id) } | ConvertTo-Json) | Out-Null
$ru = Invoke-RestMethod -Uri "$base/api/auth/login" -Method Post -ContentType "application/json" `
  -Body (@{ username = "reader"; password = "Reader123!" } | ConvertTo-Json)
$rtok = $ru.accessToken
$rlibs = Invoke-RestMethod -Uri "$base/api/libraries" -Headers (Hdr $rtok)
Write-Host "ACCESS -> reader sees $($rlibs.Count) of 2 libraries: $(($rlibs | ForEach-Object name) -join ', ')"
if ($rlibs.Count -ne 1) { throw "Expected reader to see exactly 1 library" }

# 4. Age restriction: exclude unknowns -> reader sees 0 series (samples are unrated)
Invoke-RestMethod -Uri "$base/api/users/$($u.id)" -Method Put -Headers (Hdr $tok) -ContentType "application/json" `
  -Body (@{ maxAgeRating = 3; includeUnknowns = $false } | ConvertTo-Json) | Out-Null
$rser0 = Invoke-RestMethod -Uri "$base/api/libraries/$($lib1.id)/series" -Headers (Hdr $rtok)
Write-Host "AGE (exclude unknowns) -> reader sees $($rser0.Count) series"
Invoke-RestMethod -Uri "$base/api/users/$($u.id)" -Method Put -Headers (Hdr $tok) -ContentType "application/json" `
  -Body (@{ maxAgeRating = 3; includeUnknowns = $true } | ConvertTo-Json) | Out-Null
$rser1 = Invoke-RestMethod -Uri "$base/api/libraries/$($lib1.id)/series" -Headers (Hdr $rtok)
Write-Host "AGE (include unknowns) -> reader sees $($rser1.Count) series"

# 5. Collections
$col = Invoke-RestMethod -Uri "$base/api/collections" -Method Post -Headers (Hdr $tok) -ContentType "application/json" `
  -Body (@{ name = "Favorites"; isPublic = $true } | ConvertTo-Json)
Invoke-RestMethod -Uri "$base/api/collections/$($col.id)/items/$($s0.id)" -Method Post -Headers (Hdr $tok) | Out-Null
$coldet = Invoke-RestMethod -Uri "$base/api/collections/$($col.id)" -Headers (Hdr $tok)
Write-Host "COLLECTION '$($coldet.name)' -> $($coldet.series.Count) series"

# 6. Reading lists + CBL round-trip
$rl = Invoke-RestMethod -Uri "$base/api/reading-lists" -Method Post -Headers (Hdr $tok) -ContentType "application/json" `
  -Body (@{ name = "My List"; isPublic = $false } | ConvertTo-Json)
Invoke-RestMethod -Uri "$base/api/reading-lists/$($rl.id)/items/$chapterId" -Method Post -Headers (Hdr $tok) | Out-Null
$cbl = Invoke-WebRequest -UseBasicParsing -Uri "$base/api/reading-lists/$($rl.id)/export-cbl" -Headers (Hdr $tok)
$cblXml = if ($cbl.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($cbl.Content) } else { [string]$cbl.Content }
$imp = Invoke-RestMethod -Uri "$base/api/reading-lists/import-cbl" -Method Post -Headers (Hdr $tok) -ContentType "application/json" `
  -Body (@{ name = "Imported"; xml = $cblXml } | ConvertTo-Json)
Write-Host "READING LIST -> exported CBL ($($cblXml.Length) chars); import matched=$($imp.matched) unmatched=$($imp.unmatched)"

# 7. Want to read
Invoke-RestMethod -Uri "$base/api/want-to-read/$($s0.id)" -Method Post -Headers (Hdr $tok) | Out-Null
$wtr = Invoke-RestMethod -Uri "$base/api/want-to-read" -Headers (Hdr $tok)
Write-Host "WANT-TO-READ -> $($wtr.Count) series"

# 8. Ratings + reviews
Invoke-RestMethod -Uri "$base/api/ratings" -Method Post -Headers (Hdr $tok) -ContentType "application/json" `
  -Body (@{ seriesId = $s0.id; stars = 4 } | ConvertTo-Json) | Out-Null
Invoke-RestMethod -Uri "$base/api/reviews" -Method Post -Headers (Hdr $tok) -ContentType "application/json" `
  -Body (@{ seriesId = $s0.id; body = "Great read." } | ConvertTo-Json) | Out-Null
$rated = Invoke-RestMethod -Uri "$base/api/series/$($s0.id)" -Headers (Hdr $tok)
$revs = Invoke-RestMethod -Uri "$base/api/reviews?seriesId=$($s0.id)" -Headers (Hdr $tok)
Write-Host "RATING -> avg=$($rated.averageRating) count=$($rated.ratingCount) myStars=$($rated.myStars); reviews=$($revs.Count)"

# 9. Download (admin can)
$dl = Invoke-WebRequest -UseBasicParsing -Uri "$base/api/chapters/$chapterId/download" -Headers (Hdr $tok)
Write-Host "DOWNLOAD -> status=$($dl.StatusCode) bytes=$($dl.RawContentLength)"

# 10. Stats + tasks
$ss = Invoke-RestMethod -Uri "$base/api/stats/server" -Headers (Hdr $tok)
$ms = Invoke-RestMethod -Uri "$base/api/stats/me" -Headers (Hdr $tok)
Write-Host "STATS server -> users=$($ss.users) libs=$($ss.libraries) series=$($ss.series) chapters=$($ss.chapters) pages=$($ss.totalPages)"
Write-Host "STATS me -> read=$($ms.chaptersRead) want=$($ms.wantToReadCount)"
$scanAll = Invoke-RestMethod -Uri "$base/api/tasks/scan-all" -Method Post -Headers (Hdr $tok)
$tasks = Invoke-RestMethod -Uri "$base/api/tasks" -Headers (Hdr $tok)
Write-Host "TASKS -> scan-all libs=$($scanAll.libraries); job log entries=$($tasks.Count)"

# 11. OPDS (Basic auth)
try { Invoke-WebRequest -UseBasicParsing -Uri "$base/api/opds" -ErrorAction Stop | Out-Null; Write-Host "OPDS no-auth -> UNEXPECTED 200" }
catch { Write-Host "OPDS no-auth -> $($_.Exception.Response.StatusCode.value__) (expected 401)" }
$opdsRoot = Invoke-WebRequest -UseBasicParsing -Uri "$base/api/opds" -Headers (Basic "admin" "Password123!")
$opdsLibs = Invoke-WebRequest -UseBasicParsing -Uri "$base/api/opds/libraries" -Headers (Basic "admin" "Password123!")
$opdsSeries = Invoke-WebRequest -UseBasicParsing -Uri "$base/api/opds/series/$($s0.id)" -Headers (Basic "admin" "Password123!")
Write-Host "OPDS root bytes=$($opdsRoot.RawContentLength); libraries feed bytes=$($opdsLibs.RawContentLength); series feed bytes=$($opdsSeries.RawContentLength)"

Write-Host "`nALL PHASE 3 CHECKS DONE."
