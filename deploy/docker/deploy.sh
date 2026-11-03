#!/usr/bin/env bash
set -euo pipefail
script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
command -v python3 >/dev/null || { echo '需要服务器已安装的 python3。' >&2; exit 1; }
exec python3 "$script_dir/deploy.py" "$@"
