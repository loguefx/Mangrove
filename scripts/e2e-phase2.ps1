$ErrorActionPreference = "Stop"
$base = "http://localhost:5080"
$samples = Join-Path $env:TEMP "mangrove-samples"

function Hdr($t) { @{ Authorization = "Bearer $t" } }

# 1. First-run admin (or login if already set up)
try {
  $reg = Invoke-RestMethod -Uri "$base/api/auth/register-first" -Method Post -ContentType "application/json" `
    -Body (@{ username = "admin"; email = "a@b.c"; password = "Password123!" } | ConvertTo-Json)
  Write-Host "Registered admin."
}
catch {
  $reg = Invoke-RestMethod -Uri "$base/api/auth/login" -Method Post -ContentType "application/json" `
    -Body (@{ username = "admin"; password = "Password123!" } | ConvertTo-Json)
  Write-Host "Logged in (already set up)."
}
$tok = $reg.accessToken
Write-Host "Token len: $($tok.Length)"

# 2. Create local library
$lib = Invoke-RestMethod -Uri "$base/api/libraries" -Method Post -Headers (Hdr $tok) -ContentType "application/json" `
  -Body (@{ name = "Samples"; type = 0; storageKind = 0; rootPath = $samples; credentialId = $null; folderWatch = $false } | ConvertTo-Json)
Write-Host "Created library id=$($lib.id) root=$($lib.rootPath)"

# 3. Scan
$scan = Invoke-RestMethod -Uri "$base/api/libraries/$($lib.id)/scan" -Method Post -Headers (Hdr $tok)
Write-Host "SCAN -> files=$($scan.filesSeen) added=$($scan.chaptersAdded) series=$($scan.seriesCount)"

# 4. Series + chapters per format
$series = Invoke-RestMethod -Uri "$base/api/libraries/$($lib.id)/series" -Headers (Hdr $tok)
Write-Host "`n=== SERIES ($($series.Count)) ==="
foreach ($s in ($series | Sort-Object name)) {
  $detail = Invoke-RestMethod -Uri "$base/api/series/$($s.id)" -Headers (Hdr $tok)
  foreach ($v in $detail.volumes) {
    foreach ($c in $v.chapters) {
      Write-Host ("  {0,-16} ch={1} fmt={2,-6} pages={3} cover={4}" -f $s.name, $c.number, $c.fileFormat, $c.pageCount, $c.hasCover)
    }
  }
}

# 5. Exercise readers per chapter
Write-Host "`n=== READER ENDPOINTS ==="
foreach ($s in $series) {
  $detail = Invoke-RestMethod -Uri "$base/api/series/$($s.id)" -Headers (Hdr $tok)
  foreach ($v in $detail.volumes) {
    foreach ($c in $v.chapters) {
      $man = Invoke-RestMethod -Uri "$base/api/chapters/$($c.id)/manifest" -Headers (Hdr $tok)
      if ($man.mediaType -eq "epub") {
        $bm = Invoke-RestMethod -Uri "$base/api/books/$($c.id)/manifest" -Headers (Hdr $tok)
        $href = $bm.spine[0].href
        $content = Invoke-WebRequest -UseBasicParsing -Uri "$base/api/books/$($c.id)/content/$href" -Headers (Hdr $tok)
        Write-Host ("  EPUB '{0}' spine={1} toc={2} firstHref={3} contentBytes={4}" -f $bm.title, $bm.spine.Count, $bm.toc.Count, $href, $content.RawContentLength)
      }
      else {
        $p0 = Invoke-WebRequest -UseBasicParsing -Uri "$base/api/chapters/$($c.id)/pages/0" -Headers (Hdr $tok)
        Write-Host ("  IMG  fmt={0,-6} pages={1} page0={2} ({3} bytes, {4})" -f $c.fileFormat, $man.pageCount, $p0.StatusCode, $p0.RawContentLength, $p0.Headers["Content-Type"])
      }
    }
  }
}

# 6. Search + dashboard
$srch = Invoke-RestMethod -Uri "$base/api/search?q=sample" -Headers (Hdr $tok)
Write-Host "`nSEARCH 'sample' -> $($srch.Count) result(s): $(( $srch | ForEach-Object { $_.name }) -join ', ')"

$dash = Invoke-RestMethod -Uri "$base/api/dashboard" -Headers (Hdr $tok)
Write-Host "DASHBOARD -> continueReading=$($dash.continueReading.Count) recentlyAdded=$($dash.recentlyAdded.Count)"

# 7. Metadata edit + lock
$upd = Invoke-RestMethod -Uri "$base/api/series/$($series[0].id)" -Method Put -Headers (Hdr $tok) -ContentType "application/json" `
  -Body (@{ name = $series[0].name; summary = "Edited summary wins"; genres = "Action,Drama" } | ConvertTo-Json)
Write-Host "METADATA EDIT -> '$($upd.name)' summary='$($upd.summary)'"

Write-Host "`nALL CHECKS DONE."
