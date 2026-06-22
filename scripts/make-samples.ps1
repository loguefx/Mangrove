# Generates a sample multi-format library for Phase 2 verification.
param([string]$Root = (Join-Path $env:TEMP "mangrove-samples"))

$ErrorActionPreference = "Stop"
if (Test-Path $Root) { Remove-Item $Root -Recurse -Force }
New-Item -ItemType Directory -Path $Root | Out-Null

# 1x1 PNG (red) — small but a valid decodable image.
$pngB64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="
$png = [Convert]::FromBase64String($pngB64)

function New-Images($dir, $count) {
  New-Item -ItemType Directory -Path $dir -Force | Out-Null
  for ($i = 1; $i -le $count; $i++) {
    [IO.File]::WriteAllBytes((Join-Path $dir ("{0:00}.png" -f $i)), $png)
  }
}

# --- CBZ (zip) ---
$tmp = Join-Path $Root "_tmp_cbz"
New-Images $tmp 3
$seriesDir = Join-Path $Root "Zip Manga"
New-Item -ItemType Directory -Path $seriesDir -Force | Out-Null
Compress-Archive -Path (Join-Path $tmp "*") -DestinationPath (Join-Path $seriesDir "Vol 1.zip") -Force
Move-Item (Join-Path $seriesDir "Vol 1.zip") (Join-Path $seriesDir "Vol 1.cbz") -Force
Remove-Item $tmp -Recurse -Force

# --- CBT (tar via tar.exe) — exercises SharpCompress non-zip path ---
$tmp = Join-Path $Root "_tmp_tar"
New-Images $tmp 4
$seriesDir = Join-Path $Root "Tar Comic"
New-Item -ItemType Directory -Path $seriesDir -Force | Out-Null
tar -cf (Join-Path $seriesDir "Issue 1.cbt") -C $tmp .
Remove-Item $tmp -Recurse -Force

# --- Raw image folder chapter ---
New-Images (Join-Path $Root "Webtoon Series\Chapter 1") 5

# --- EPUB ---
$epub = Join-Path $Root "_tmp_epub"
New-Item -ItemType Directory -Path (Join-Path $epub "META-INF") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $epub "OEBPS") -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $epub "mimetype"), "application/epub+zip")
@'
<?xml version="1.0"?>
<container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
  <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
</container>
'@ | Set-Content (Join-Path $epub "META-INF\container.xml") -Encoding UTF8
[IO.File]::WriteAllBytes((Join-Path $epub "OEBPS\cover.png"), $png)
@'
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="bookid">
  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
    <dc:identifier id="bookid">urn:uuid:mangrove-sample-1</dc:identifier>
    <dc:title>Sample EPUB Book</dc:title>
    <dc:creator>Mangrove Tester</dc:creator>
    <dc:language>en</dc:language>
    <dc:publisher>Mangrove Press</dc:publisher>
    <dc:description>A tiny sample book for Phase 2 testing.</dc:description>
    <meta name="cover" content="cover-image"/>
  </metadata>
  <manifest>
    <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
    <item id="cover-image" href="cover.png" media-type="image/png" properties="cover-image"/>
    <item id="ch1" href="ch1.xhtml" media-type="application/xhtml+xml"/>
    <item id="ch2" href="ch2.xhtml" media-type="application/xhtml+xml"/>
    <item id="css" href="style.css" media-type="text/css"/>
  </manifest>
  <spine>
    <itemref idref="ch1"/>
    <itemref idref="ch2"/>
  </spine>
</package>
'@ | Set-Content (Join-Path $epub "OEBPS\content.opf") -Encoding UTF8
@'
<?xml version="1.0" encoding="utf-8"?>
<html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
<head><title>Contents</title></head>
<body><nav epub:type="toc"><ol>
  <li><a href="ch1.xhtml">Chapter One</a></li>
  <li><a href="ch2.xhtml">Chapter Two</a></li>
</ol></nav></body></html>
'@ | Set-Content (Join-Path $epub "OEBPS\nav.xhtml") -Encoding UTF8
"body{font-family:serif} h1{color:#0a7d72}" | Set-Content (Join-Path $epub "OEBPS\style.css") -Encoding UTF8
@'
<?xml version="1.0" encoding="utf-8"?>
<html xmlns="http://www.w3.org/1999/xhtml"><head><link rel="stylesheet" href="style.css"/><title>Chapter One</title></head>
<body><h1>Chapter One</h1><p>Hello from the first chapter. <img src="cover.png" alt="img"/></p></body></html>
'@ | Set-Content (Join-Path $epub "OEBPS\ch1.xhtml") -Encoding UTF8
@'
<?xml version="1.0" encoding="utf-8"?>
<html xmlns="http://www.w3.org/1999/xhtml"><head><link rel="stylesheet" href="style.css"/><title>Chapter Two</title></head>
<body><h1>Chapter Two</h1><p>The second chapter continues the story.</p></body></html>
'@ | Set-Content (Join-Path $epub "OEBPS\ch2.xhtml") -Encoding UTF8

$bookDir = Join-Path $Root "Sample Book"
New-Item -ItemType Directory -Path $bookDir -Force | Out-Null

# EPUB requires forward-slash entry names and an uncompressed "mimetype" first entry.
# Compress-Archive writes backslashes, so build the zip with System.IO.Compression directly.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$epubFile = Join-Path $bookDir "book.epub"
if (Test-Path $epubFile) { Remove-Item $epubFile -Force }
$fs = [IO.File]::Open($epubFile, [IO.FileMode]::Create)
$zip = New-Object System.IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create)
try {
  $mt = $zip.CreateEntry("mimetype", [IO.Compression.CompressionLevel]::NoCompression)
  $s = $mt.Open(); $b = [Text.Encoding]::ASCII.GetBytes("application/epub+zip"); $s.Write($b, 0, $b.Length); $s.Dispose()
  Get-ChildItem $epub -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($epub.Length + 1).Replace('\', '/')
    $e = $zip.CreateEntry($rel, [IO.Compression.CompressionLevel]::Optimal)
    $es = $e.Open(); $by = [IO.File]::ReadAllBytes($_.FullName); $es.Write($by, 0, $by.Length); $es.Dispose()
  }
}
finally { $zip.Dispose(); $fs.Dispose() }
Remove-Item $epub -Recurse -Force

# --- PDF (valid single-page with a correct, byte-accurate xref table) ---
$nl = "`n"
$content = "BT /F1 24 Tf 40 120 Td (Hello Mangrove PDF) Tj ET"
$objs = @(
  "<</Type/Catalog/Pages 2 0 R>>",
  "<</Type/Pages/Kids[3 0 R]/Count 1>>",
  "<</Type/Page/Parent 2 0 R/MediaBox[0 0 300 200]/Contents 4 0 R/Resources<</Font<</F1 5 0 R>>>>>>",
  "<</Length $($content.Length)>>${nl}stream${nl}${content}${nl}endstream",
  "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>"
)
$sb = New-Object System.Text.StringBuilder
[void]$sb.Append("%PDF-1.4$nl")
$offsets = @()
for ($i = 0; $i -lt $objs.Count; $i++) {
  $offsets += $sb.Length
  [void]$sb.Append("$($i+1) 0 obj$nl$($objs[$i])${nl}endobj$nl")
}
$xrefPos = $sb.Length
$size = $objs.Count + 1
[void]$sb.Append("xref${nl}0 $size$nl")
[void]$sb.Append("0000000000 65535 f`r`n")
foreach ($off in $offsets) { [void]$sb.Append(("{0:D10} 00000 n`r`n" -f $off)) }
[void]$sb.Append("trailer$nl<</Size $size/Root 1 0 R>>${nl}startxref$nl$xrefPos$nl%%EOF")
$pdfDir = Join-Path $Root "Sample PDF"
New-Item -ItemType Directory -Path $pdfDir -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $pdfDir "doc.pdf"), $sb.ToString(), [Text.Encoding]::ASCII)

Write-Host "Sample library created at: $Root"
Get-ChildItem $Root -Recurse -File | ForEach-Object { Write-Host (" - " + $_.FullName.Substring($Root.Length+1)) }
