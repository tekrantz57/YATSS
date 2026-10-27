[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^COM[0-9]+$')][string]$Port)

$ErrorActionPreference = "Stop"
function Checksum([string]$Body) {
    $sum = 0
    foreach ($value in [Text.Encoding]::ASCII.GetBytes($Body)) { $sum = $sum -bxor $value }
    return $sum
}
$serial = [System.IO.Ports.SerialPort]::new($Port, 115200)
$serial.ReadTimeout = 200
$serial.WriteTimeout = 1000
$serial.NewLine = "`n"
$serial.DtrEnable = $true
$serial.RtsEnable = $true
function Send([string]$Body) {
    $serial.WriteLine("$Body*{0:X2}" -f (Checksum $Body))
}
$session = $null
$heartbeats = 0
$offConfirmed = $false
$frames = 0
try {
    $serial.Open()
    $serial.DiscardInBuffer()
    Send "PING"
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $nextCommand = 1000
    while ($clock.ElapsedMilliseconds -lt 12000) {
        if ($clock.ElapsedMilliseconds -ge $nextCommand) {
            if ($session) { Send "KEEPALIVE:$session" } else { Send "PING" }
            $nextCommand = $clock.ElapsedMilliseconds + 1000
        }
        try { $line = $serial.ReadLine().Trim() }
        catch [TimeoutException] { continue }
        if (-not $line) { continue }
        Write-Output $line
        if ($line -notmatch '^(?<body>.+)\*(?<checksum>[0-9a-fA-F]{2})$') { throw "Malformed controller frame." }
        $body = $Matches.body
        if ((Checksum $body) -ne [Convert]::ToInt32($Matches.checksum, 16)) { throw "Bad controller checksum." }
        $frames++
        if ($body -match '^HELLO:YATSSMC:5:8:RASPBERRY_PI_PICO_2_W:[^:]+:4194304:([0-9A-F]{16})$') {
            $found = $Matches[1]
            if ($found -eq '0000000000000000') { throw "Controller has no valid boot session." }
            if ($session -and $session -ne $found) { throw "Unexpected controller reset during check." }
            $session = $found
            Send "TRACK_POWER:MASK:00:$session"
        }
        if ($session) {
            if (-not $body.EndsWith(":$session")) { throw "Unexpected controller session." }
            if ($body -match '^HEARTBEAT:[0-9]+:') { $heartbeats++ }
            if ($body -eq "HELLO:TRACK_POWER:MASK:00:$session") { $offConfirmed = $true }
            if ($body.StartsWith('ERR:')) { throw "Controller reported an error: $body" }
        }
    }
    if (-not $session -or -not $offConfirmed -or $heartbeats -lt 3) {
        throw "Pico check incomplete: identity=$session, off=$offConfirmed, heartbeats=$heartbeats."
    }
    Write-Output "PASS: Pico 2 W identity, session, $frames checksummed frames, $heartbeats heartbeats and power-OFF acknowledgement."
    Write-Output "No power-ON commands sent. GPIO levels/sensors/relays are NOT verified by this check."
}
finally {
    if ($serial.IsOpen -and $session) {
        try { Send "TRACK_POWER:MASK:00:$session" } catch { Write-Warning "Final OFF command failed: $_" }
    }
    $serial.Dispose()
}
