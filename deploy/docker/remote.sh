#!/usr/bin/env bash
set -euo pipefail
script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
command -v python3 >/dev/null || { echo '本机需要 Python 3.9 或以上版本。' >&2; exit 1; }
python3 -c 'import sys; sys.exit(sys.version_info < (3, 9))' || { echo '本机 Python 需为 3.9 或以上。' >&2; exit 1; }
if [[ ${1:-} == --help || ${1:-} == -h ]]; then
  exec python3 "$script_dir/remote.py" --help
fi
for tool in ssh sftp; do
  command -v "$tool" >/dev/null || { echo "缺少系统命令: $tool" >&2; exit 1; }
done
exec python3 -u "$script_dir/remote.py" "$@"
