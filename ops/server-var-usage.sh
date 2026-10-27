#!/usr/bin/env bash
# ---------------------------------------------------------------------------
#  Read-only: what is filling /var on the server. Changes nothing.
#    cmd /c "ssh os-titpooja@10.204.212.28 bash -s < D:\DTMS\ops\server-var-usage.sh > D:\server-var-usage.txt 2>&1"
#
#  /var/lib/docker and /var/lib/containerd are root-only, so the last section
#  reads them through a throwaway container that mounts them READ-ONLY, with no
#  network, using an image already on the box (nothing is pulled).
# ---------------------------------------------------------------------------

section() { printf '\n===== %s =====\n' "$*"; }

section '/var'
df -h /var

section 'biggest folders under /var this user can read'
du -xh --max-depth=3 /var 2>/dev/null | sort -rh | head -25

section 'docker system df (summary)'
docker system df 2>&1

section 'docker system df -v (images / containers / volumes / build cache)'
docker system df -v 2>&1

section 'images no container uses (running or stopped)'
used=$(docker ps -a --format '{{.Image}}' | sort -u)
docker images --format '{{.Repository}}:{{.Tag}}\t{{.ID}}\t{{.Size}}\t{{.CreatedSince}}' |
  while IFS=$'\t' read -r ref id size age; do
    grep -qxF "$ref" <<<"$used" || grep -qxF "$id" <<<"$used" || printf '%s\t%s\t%s\n' "$size" "$ref" "$age"
  done

section 'stopped containers'
docker ps -a --filter status=exited --filter status=created --format '{{.Names}}\t{{.Image}}\t{{.Status}}\t{{.Size}}' 2>&1

section 'journal'
journalctl --disk-usage 2>&1

section 'root-only folders, read through a read-only container'
IMG=$(docker images --format '{{.Repository}}:{{.Tag}}' | grep -E '^(postgres|redis|nginx):' | head -n1)
if [ -z "$IMG" ]; then
  echo 'no suitable local image; skipped'
else
  echo "(using $IMG)"
  docker run --rm --network none --entrypoint sh \
    -v /var:/v:ro "$IMG" -c '
      echo "--- /var top level"; du -xsh /v/* 2>/dev/null | sort -rh | head -15
      echo "--- /var/lib";       du -xsh /v/lib/* 2>/dev/null | sort -rh | head -10
      echo "--- docker";         du -xsh /v/lib/docker/* 2>/dev/null | sort -rh
      echo "--- containerd";     du -xsh /v/lib/containerd/* 2>/dev/null | sort -rh | head -8
      echo "--- /var/log";       du -xsh /v/log/* 2>/dev/null | sort -rh | head -10
      echo "--- container logs (json-file), biggest first"
      for f in /v/lib/docker/containers/*/*-json.log; do [ -f "$f" ] && du -h "$f"; done 2>/dev/null | sort -rh | head -15
    ' 2>&1
  echo '--- container id -> name'
  docker ps -a --no-trunc --format '{{.ID}}  {{.Names}}'
fi
