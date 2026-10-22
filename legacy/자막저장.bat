<# : batch
@echo off
chcp 65001 >nul
set "CAP_DIR=%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -Command "iex ((Get-Content -LiteralPath '%~f0' -Encoding UTF8) -join [char]10)"
pause
exit /b
#>
# Chrome(또는 Edge) 실시간 자막 말풍선의 텍스트를 읽어 txt에 누적 저장 (Windows 10/11)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE   = [System.Windows.Automation.AutomationElement]
$TS   = [System.Windows.Automation.TreeScope]
$Cond = [System.Windows.Automation.Condition]::TrueCondition

$dir  = if ($env:CAP_DIR) { $env:CAP_DIR } else { (Get-Location).Path }
$out  = Join-Path $dir ("자막_" + (Get-Date -Format 'yyyyMMdd_HHmmss') + ".txt")
$diag = Join-Path $dir "자막_진단.txt"
$utf8 = New-Object System.Text.UTF8Encoding($true)

$TitleRx   = '(?i)^(실시간 자막|live caption)'
$BrowserRx = ' - (Google Chrome|Chrome|Microsoft.? Edge|Whale)$'
$script:bubble = $null

function Get-BrowserTopWindows {
    $ids = @(Get-Process chrome, msedge, whale -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
    if (-not $ids) { return @() }
    $res = @()
    foreach ($w in $AE::RootElement.FindAll($TS::Children, $Cond)) {
        try {
            $c = $w.Current
            if ($ids -contains $c.ProcessId -and $c.ClassName -like 'Chrome_WidgetWin*') { $res += $w }
        } catch {}
    }
    return $res
}

# 말풍선 안에서 자막 본문(가장 긴 텍스트, 제목·버튼 제외)을 꺼냄
function Read-Bubble($w) {
    $best = ''
    $found = $false
    foreach ($e in $w.FindAll($TS::Descendants, $Cond)) {
        $c = $e.Current
        $n = $c.Name
        if (-not $n) { continue }
        if ($n -match $TitleRx) { $found = $true; continue }
        if ($c.ControlType -eq [System.Windows.Automation.ControlType]::Button) { continue }
        if ($n.Length -gt $best.Length) { $best = $n }
    }
    if ($found -or $w.Current.Name -match $TitleRx) { return $best }
    return $null
}

function Get-CaptionText {
    if ($script:bubble) {
        try { $t = Read-Bubble $script:bubble; if ($null -ne $t) { return $t } } catch {}
        $script:bubble = $null
    }
    foreach ($w in Get-BrowserTopWindows) {
        try {
            # 일반 브라우저 창은 건너뜀 (웹페이지 전체를 훑으면 느려짐)
            if ($w.Current.Name -match $BrowserRx) { continue }
            $t = Read-Bubble $w
            if ($null -ne $t) { $script:bubble = $w; return $t }
        } catch {}
    }
    return $null
}

function Write-Diag {
    $sb = New-Object System.Text.StringBuilder
    foreach ($w in Get-BrowserTopWindows) {
        try {
            $c = $w.Current
            [void]$sb.AppendLine("== TOP name=[$($c.Name)] cls=$($c.ClassName) rect=$($c.BoundingRectangle)")
            if ($c.Name -match $BrowserRx) { continue }
            foreach ($e in $w.FindAll($TS::Descendants, $Cond)) {
                $d = $e.Current
                [void]$sb.AppendLine("   $($d.ControlType.ProgrammaticName) | id=$($d.AutomationId) | cls=$($d.ClassName) | name=[$($d.Name)]")
            }
        } catch {}
    }
    [System.IO.File]::WriteAllText($diag, $sb.ToString(), $utf8)
}

# ---- 누적 로직: 말풍선은 최근 몇 줄만 보여주며 계속 밀려 올라가므로,
#      이미 저장한 끝부분을 새 텍스트에서 찾아 그 뒤만 이어 붙임.
#      끝의 몇 글자는 아직 인식이 고쳐질 수 있어 보류했다가 잠잠해지면 저장.
$script:saved = ''        # 지금까지 저장한 전체(끝부분 비교용, 최근 2000자만 유지)
$HOLD = 12                # 보류할 끝 글자 수

function Normalize([string]$s) { return (($s -replace '\s+', ' ').Trim()) }

function Append-Out([string]$chunk) {
    if (-not $chunk) { return }
    $script:saved += $chunk
    if ($script:saved.Length -gt 2000) { $script:saved = $script:saved.Substring($script:saved.Length - 2000) }
    # 문장 끝(. ? !) 뒤에서 줄바꿈해 읽기 좋게
    $pretty = $chunk -replace '([.?!。？！])\s+', "`$1`r`n"
    [System.IO.File]::AppendAllText($out, $pretty, $utf8)
    Write-Host -NoNewline $pretty
}

# 새 텍스트에서 아직 저장 안 된 부분을 반환
function Get-NewPart([string]$t) {
    if (-not $script:saved) { return $t }
    foreach ($len in 40, 25, 15, 8) {
        if ($script:saved.Length -lt $len) { continue }
        $tail = $script:saved.Substring($script:saved.Length - $len)
        $i = $t.LastIndexOf($tail)
        if ($i -ge 0) { return $t.Substring($i + $len) }
    }
    # 이어지는 지점을 못 찾음 = 자막이 새로 시작됨
    return "`r`n" + $t
}

function Commit([string]$t, [bool]$all) {
    $new = Get-NewPart $t
    if (-not $all) {
        if ($new.Length -le $HOLD) { return }
        $cut = $new.Length - $HOLD
        $sp = $new.LastIndexOf(' ', $cut)       # 단어 중간에서 자르지 않도록
        if ($sp -gt 0) { $cut = $sp + 1 }
        $new = $new.Substring(0, $cut)
    }
    Append-Out $new
}

Write-Host "저장 파일: $out" -ForegroundColor Cyan
Write-Host "크롬 설정 > 접근성 > 실시간 자막 을 켜고 영상/소리를 재생하세요. (종료: 창 닫기 또는 Ctrl+C)" -ForegroundColor Cyan

$state = ''
$last = ''
$lastChange = Get-Date
$flushed = $true
$start = Get-Date
$diagDone = $false
try {
    while ($true) {
        $raw = Get-CaptionText
        if ($null -eq $raw) {
            if ($state -ne 'none') { Write-Host "`n[자막 말풍선을 찾는 중...]" -ForegroundColor Yellow; $state = 'none' }
            if (-not $diagDone -and ((Get-Date) - $start).TotalSeconds -gt 20) {
                Write-Diag; $diagDone = $true
                Write-Host "[20초 동안 못 찾음 -> 창 정보 저장: $diag]" -ForegroundColor Yellow
            }
        } else {
            if ($state -ne 'ok') { Write-Host "[자막 말풍선 연결됨]" -ForegroundColor Green; $state = 'ok' }
            $t = Normalize $raw
            if ($t -and $t -ne $last) {
                Commit $t $false
                $last = $t; $lastChange = Get-Date; $flushed = $false
            } elseif (-not $flushed -and ((Get-Date) - $lastChange).TotalSeconds -gt 3) {
                Commit $last $true          # 3초간 변화 없으면 보류분까지 저장
                $flushed = $true
            }
        }
        Start-Sleep -Milliseconds 500
    }
} finally {
    if (-not $flushed -and $last) { Commit $last $true }
    Write-Host "`n저장 완료: $out" -ForegroundColor Cyan
}
