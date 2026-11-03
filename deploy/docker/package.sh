#!/usr/bin/env bash
# Build on macOS/Linux; the output is an offline server deployment bundle.
set -euo pipefail

if [[ ${1:-} == --help || $# != 1 ]]; then
  echo '用法: bash deploy/docker/package.sh <版本号，例如 20261027-01>'
  [[ ${1:-} == --help ]] && exit 0
  exit 1
fi
release=$1
if [[ ! $release =~ ^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$ || $release == latest ]]; then
  echo '版本号必须是明确的 Docker 标签，不能使用 latest。' >&2
  exit 1
fi
script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
repo_dir=$(cd "$script_dir/../.." && pwd)
cd "$repo_dir"
for command in docker python3; do
  command -v "$command" >/dev/null || { echo "缺少命令: $command" >&2; exit 1; }
done
docker info >/dev/null
docker buildx version >/dev/null
output_dir="$repo_dir/output/mom-deploy"
mkdir -p "$output_dir"
bundle_name="mom-backend-$release"
archive="$output_dir/$bundle_name.tar.gz"
[[ ! -e $archive ]] || { echo '该版本离线包已存在，请使用新的版本号。' >&2; exit 1; }
for service in identity gateway; do
  if docker image inspect "mom-$service:$release" >/dev/null 2>&1; then
    echo "镜像 mom-$service:$release 已存在，请使用新的版本号，避免覆盖发布记录。" >&2
    exit 1
  fi
done
stage=$(mktemp -d "$output_dir/.package.XXXXXX")
trap 'rm -rf "$stage"' EXIT
bundle_dir="$stage/$bundle_name"
mkdir -p "$bundle_dir/templates"

docker buildx build --platform linux/amd64 \
  --file Services/Identity/ZX.Platform.Identity.Api/Dockerfile \
  --tag "mom-identity:$release" --load .
docker buildx build --platform linux/amd64 \
  --file ZX.Platform.Gateway/Dockerfile \
  --tag "mom-gateway:$release" --load .

python3 - "$release" "$bundle_dir/manifest.json" <<'PY'
import datetime
import json
import subprocess
import sys
release, output = sys.argv[1:]
images = {}
for service in ('identity', 'gateway'):
    tag = f'mom-{service}:{release}'
    info = json.loads(subprocess.check_output(['docker', 'image', 'inspect', tag]))[0]
    if (info['Os'], info['Architecture']) != ('linux', 'amd64'):
        raise SystemExit(f'{tag}: image must be linux/amd64')
    images[service] = {'tag': tag, 'id': info['Id']}
revision = subprocess.run(['git', 'rev-parse', 'HEAD'], text=True, capture_output=True)
dirty = subprocess.run(['git', 'status', '--porcelain'], text=True, capture_output=True)
with open(output, 'w') as stream:
    json.dump({'format': 1, 'release': release, 'images': images,
               'created_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
               'git_revision': revision.stdout.strip(), 'working_tree_dirty': bool(dirty.stdout)},
              stream, indent=2)
PY
docker save --output "$bundle_dir/images.tar" "mom-identity:$release" "mom-gateway:$release"
# Explicit allowlist: never copy server config, User Secrets or arbitrary output files.
cp "$script_dir/deploy.sh" "$script_dir/deploy.py" "$bundle_dir/"
cp "$script_dir/README.md" "$bundle_dir/README.md"
cp "$script_dir/templates/identity.env.example" "$bundle_dir/templates/"
cp "$script_dir/templates/gateway.env.example" "$bundle_dir/templates/"
cp "$script_dir/templates/deploy.json.example" "$bundle_dir/templates/"
python3 - "$bundle_dir" <<'PY'
import hashlib
from pathlib import Path
import sys
root = Path(sys.argv[1])
with (root / 'SHA256SUMS').open('w') as stream:
    for path in sorted(root.rglob('*')):
        if path.is_file() and path.name != 'SHA256SUMS':
            digest = hashlib.sha256()
            with path.open('rb') as source:
                for block in iter(lambda: source.read(1024 * 1024), b''):
                    digest.update(block)
            stream.write(f'{digest.hexdigest()}  {path.relative_to(root).as_posix()}\n')
PY
python3 - "$stage" "$bundle_name" <<'PY'
import sys
import tarfile
from pathlib import Path

stage = Path(sys.argv[1])
bundle_name = sys.argv[2]
source = stage / bundle_name
archive = stage / (bundle_name + '.tar.gz')

# Python's tar writer avoids macOS AppleDouble (._*) metadata entries.
with tarfile.open(archive, 'w:gz', format=tarfile.PAX_FORMAT) as output:
    output.add(source, arcname=bundle_name, recursive=True)
PY
mv "$stage/$bundle_name.tar.gz" "$archive"
echo "已生成: $archive"
echo "上传并解压后，首次执行 sudo bash deploy.sh init；配置好后执行 sudo bash deploy.sh。"
