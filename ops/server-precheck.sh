#!/usr/bin/env bash
# ---------------------------------------------------------------------------
#  Read-only check of a server before ops/server-install.sh goes on it.
#  Changes nothing. Run it from the Windows PC (asks for the SSH password):
#
#    cmd /c "ssh <user>@10.204.212.28 bash -s < ops\server-precheck.sh > D:\server-precheck.txt 2>&1"
#
#  stdin is the script, so sudo cannot prompt; everything that needs root is
#  tried with `sudo -n` and reported as skipped when a password would be asked.
# ---------------------------------------------------------------------------

section() { printf '\n===== %s =====\n' "$*"; }
try()     { "$@" 2>&1 || echo "(exit $?)"; }
root()    { if sudo -n true 2>/dev/null; then sudo -n "$@" 2>&1; else echo "(skipped: sudo needs a password)"; fi; }

section 'identity'
echo "host: $(hostname)  user: $(id -un)  groups: $(id -Gn)"
sudo -n true 2>/dev/null && echo 'sudo: passwordless' || echo 'sudo: needs a password (or not allowed)'
date -Is

section 'os / cpu / memory'
grep -E '^(PRETTY_NAME|ID|VERSION_ID)=' /etc/os-release 2>/dev/null
echo "arch: $(uname -m)  kernel: $(uname -r)  cpus: $(nproc)"
free -h
command -v getenforce >/dev/null && echo "selinux: $(getenforce)" || echo 'selinux: not installed'

section 'disk'
try df -h / /var/lib/docker /opt /home

section 'docker'
if command -v docker >/dev/null; then
  try docker --version
  echo -n 'compose plugin: '; docker compose version --short 2>&1 || true
  echo -n 'docker-compose v1: '; (command -v docker-compose >/dev/null && docker-compose version --short 2>&1) || echo 'absent'
  echo "systemd: $(systemctl is-active docker 2>&1) / $(systemctl is-enabled docker 2>&1)"
  if docker info >/dev/null 2>&1; then D=docker; else D='root docker'; echo '(docker needs root for this user)'; fi
  echo '--- info'
  $D info --format 'root={{.DockerRootDir}} driver={{.Driver}} cgroup={{.CgroupDriver}} containers={{.Containers}} images={{.Images}}' 2>&1
  echo '--- running containers'
  $D ps --format '{{.Names}}\t{{.Image}}\t{{.Ports}}' 2>&1
  echo '--- networks'
  for n in $($D network ls -q 2>/dev/null); do
    $D network inspect -f '{{.Name}}  {{range .IPAM.Config}}{{.Subnet}} {{end}}' "$n" 2>&1
  done
  echo '--- dtms volumes already present (must be none)'
  $D volume ls -q 2>/dev/null | grep '^dtms_' || echo 'none'
else
  echo 'docker: NOT INSTALLED'
  command -v podman >/dev/null && echo "podman: $(podman --version)"
fi

section 'listening TCP ports'
try ss -ltn
echo '--- with process names'
root ss -ltnp

section 'ports DTMS needs on the LAN'
for p in 3002 5219 3001; do
  if ss -ltnH "sport = :$p" 2>/dev/null | grep -q .; then echo "$p IN USE"; else echo "$p free"; fi
done

section 'firewall'
if command -v firewall-cmd >/dev/null; then
  echo "firewalld: $(firewall-cmd --state 2>&1)"; root firewall-cmd --list-all
elif command -v ufw >/dev/null; then
  root ufw status
else
  echo 'no firewalld/ufw'; root iptables -S INPUT
fi

section 'routes (172.30.0.0/24 must not be in use)'
try ip -4 route
ip -4 route | grep -q '172\.30\.' && echo 'WARNING: a 172.30.x route exists' || echo '172.30.x: unused'

section 'internet'
if command -v curl >/dev/null; then
  curl -sS -m 6 -o /dev/null -w 'registry-1.docker.io: HTTP %{http_code}\n' https://registry-1.docker.io/v2/ 2>&1 || true
else
  echo 'curl: absent'
fi

section 'tools the installer uses'
for t in sha256sum tar sed sort grep; do printf '%-10s %s\n' "$t" "$(command -v $t || echo MISSING)"; done
