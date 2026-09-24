# ---------------------------------------------------------------------------
#  Publishes the DTMS stack on this PC's LAN address while it runs under podman.
#
#  Why: the stack lives inside the podman WSL machine, and WSL forwards only
#  localhost — a tablet hitting http://<this-pc>:3000 gets nothing. The usual
#  fix (`netsh interface portproxy`) needs admin, and WSL mirrored networking,
#  which would solve it with config alone, makes podman's sshd collide with
#  podman's own Windows-side forwarder on port 49682 and takes the engine down.
#
#  So Windows listens on the LAN address and tunnels into the machine over SSH.
#  The inbound rules "DTMS Frontend (3000)" and "DTMS API (5219)" already allow
#  any program on those ports, so nothing else needs opening.
#
#  The LAN address is bound explicitly rather than 0.0.0.0 because wslrelay
#  already holds 127.0.0.1:3000 and :5219 for localhost access.
#
#  The SSH port is re-read from podman on every attempt, so a machine restart
#  that reassigns it is picked up on its own.
#
#  Delete this and its Startup shortcut once Docker Desktop is back.
# ---------------------------------------------------------------------------

$ErrorActionPreference = 'SilentlyContinue'

$SshExe   = "$env:WINDIR\System32\OpenSSH\ssh.exe"
$Identity = "$env:USERPROFILE\.local\share\containers\podman\machine\machine"
$Podman   = "$env:LOCALAPPDATA\Programs\Podman\podman.exe"
$Ports    = @(3000, 5219)  # frontend, api — the two with firewall rules

function Get-SshPort {
    # Not a constant: `podman machine start` silently moves the SSH port when
    # the old one is taken ("detected port conflict ... reassigning"), and a
    # hard-coded 49682 left the tunnel failing every few seconds after that.
    & $Podman machine inspect --format '{{.SSHConfig.Port}}' 2>$null
}

function Get-LanAddress {
    # The adapter that actually carries traffic: up, and holding a default
    # gateway. Picking "the first IPv4" instead would sometimes return a
    # virtual-switch or VPN address that no other machine can reach.
    (Get-NetIPConfiguration |
        Where-Object { $_.IPv4DefaultGateway -and $_.NetAdapter.Status -eq 'Up' } |
        Select-Object -First 1).IPv4Address.IPAddress
}

while ($true) {
    $ip = Get-LanAddress
    $SshPort = Get-SshPort
    if (-not $ip -or -not $SshPort) { Start-Sleep -Seconds 20; continue }

    $forwards = $Ports | ForEach-Object { '-L', "${ip}:${_}:127.0.0.1:$_" }
    # And the other way: windows_exporter (Windows :9182) into the VM's
    # loopback, where prometheus finds it as host.docker.internal. The VM's
    # own name for Windows is a stale DHCP address, and its gateway IP can
    # change across reboots — the tunnel has neither problem.
    # The Windows end is [::1], not 127.0.0.1: WSL mirrors the VM's loopback
    # listeners onto Windows 127.0.0.1, so once this tunnel listens in the VM,
    # Windows 127.0.0.1:9182 leads straight back into it — a loop.
    $forwards += '-R', '127.0.0.1:9182:[::1]:9182'
    $sshArgs  = @(
        '-i', $Identity, '-p', $SshPort, '-N',
        '-o', 'StrictHostKeyChecking=no',
        '-o', 'UserKnownHostsFile=NUL',
        '-o', 'ExitOnForwardFailure=yes',
        '-o', 'ServerAliveInterval=30',
        '-o', 'ServerAliveCountMax=3'
    ) + $forwards + 'user@127.0.0.1'

    # A killed ssh.exe leaves its sshd-session in the VM still holding :9182
    # until the server notices, and meanwhile every retry fails the -R — which
    # ExitOnForwardFailure turns into dead LAN ports too. No tunnel of ours is
    # alive at this point, so whatever holds the port is stale.
    & $Podman machine ssh "sudo fuser -k 9182/tcp" 2>$null | Out-Null

    $ssh = Start-Process -FilePath $SshExe -ArgumentList $sshArgs -WindowStyle Hidden -PassThru
    if (-not $ssh) { Start-Sleep -Seconds 20; continue }

    # Watch two things, because only one of them makes ssh exit on its own:
    # the tunnel dying (podman restarted, machine stopped) and the LAN address
    # changing. A new DHCP lease leaves ssh happily connected to 127.0.0.1
    # while its listener sits on an address this PC no longer answers to, so
    # the address is re-checked and the process replaced when it moves.
    while (-not $ssh.HasExited) {
        Start-Sleep -Seconds 15
        if ((Get-LanAddress) -ne $ip) {
            Stop-Process -Id $ssh.Id -Force
            break
        }
    }

    Start-Sleep -Seconds 5
}
