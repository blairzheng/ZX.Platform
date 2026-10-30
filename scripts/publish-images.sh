#!/usr/bin/env bash
# 本地构建并上传后端镜像包；服务器发布与回滚由 mom_img/.gitlab-ci.yml 执行。
set -euo pipefail
set +x
umask 077

usage() {
  printf '用法：bash scripts/publish-images.sh [--package-only|--upload-only] <版本号>\n'
}
if [[ $# == 1 && $1 == --help ]]; then
  usage
  exit 0
fi
mode=publish
case "${1:-}" in
  --package-only) mode=package; shift ;;
  --upload-only) mode=upload; shift ;;
esac
if [[ $# != 1 ]]; then
  usage >&2
  exit 1
fi
release=$1
[[ $release =~ ^[A-Za-z0-9][A-Za-z0-9_.-]{0,79}$ && $release != latest ]] || {
  printf '请使用明确的版本号，例如 20261029-02，不能使用 latest。\n' >&2
  exit 1
}
repo_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_dir"
out_dir="$repo_dir/output"
bundle="mom-images-$release"
archive_name="$bundle.tar.gz"
archive="$out_dir/$archive_name"

for tool in tar gzip shasum; do
  command -v "$tool" >/dev/null || { printf '缺少工具：%s\n' "$tool" >&2; exit 1; }
done
if [[ $mode != package ]]; then
  command -v curl >/dev/null || { printf '本机缺少 curl。\n' >&2; exit 1; }
fi
mkdir -p "$out_dir"
stage=$(mktemp -d "$out_dir/.mom-images.XXXXXX")
cleanup() { unset MOM_UPLOAD_TOKEN; rm -rf -- "$stage"; }
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
trap 'exit 129' HUP

if [[ $mode != upload ]]; then
  [[ ! -e $archive ]] || {
    printf '该版本包已存在；换版本号，或用 --upload-only 重传。\n' >&2
    exit 1
  }
  command -v docker >/dev/null || { printf '本机缺少 Docker。\n' >&2; exit 1; }
  docker info >/dev/null || { printf '请先启动本机 Docker。\n' >&2; exit 1; }
  docker buildx version >/dev/null || { printf '本机 Docker 缺少 buildx。\n' >&2; exit 1; }
  printf '本地构建后端镜像：%s（linux/amd64）\n' "$release"
  docker buildx build --platform linux/amd64 \
    -f Services/Identity/ZX.Platform.Identity.Api/Dockerfile \
    -t "mom-identity:$release" --load .
  docker buildx build --platform linux/amd64 \
    -f ZX.Platform.Gateway/Dockerfile \
    -t "mom-gateway:$release" --load .

  mkdir "$stage/$bundle"
  docker save "mom-identity:$release" "mom-gateway:$release" |
    gzip > "$stage/$bundle/images.tar.gz"
  if [[ $(uname -s) == Darwin ]]; then
    COPYFILE_DISABLE=1 tar --format=ustar --no-xattrs --no-mac-metadata \
      -czf "$stage/$archive_name" -C "$stage" "$bundle"
  else
    tar --format=ustar -czf "$stage/$archive_name" -C "$stage" "$bundle"
  fi
  mv "$stage/$archive_name" "$archive"
fi

[[ -f $archive && ! -L $archive && -s $archive ]] || {
  printf '找不到版本包：%s\n' "$archive" >&2
  exit 1
}
gzip -t "$archive"
tar -tzf "$archive" "$bundle/images.tar.gz" >/dev/null
digest=$(shasum -a 256 "$archive")
digest=${digest%% *}
printf '%s  %s\n' "$digest" "$archive_name" > "$archive.sha256"
printf '\n版本包：%s\n' "$archive"
ls -lh "$archive"

if [[ $mode != package ]]; then
  if [[ -z ${MOM_UPLOAD_TOKEN:-} ]]; then
    read -r -s -p '请输入 Package Registry 的 Deploy Token（粘贴后按回车）：' MOM_UPLOAD_TOKEN < /dev/tty
    printf '\n'
  fi
  [[ -n $MOM_UPLOAD_TOKEN && $MOM_UPLOAD_TOKEN != *$'\n'* && $MOM_UPLOAD_TOKEN != *$'\r'* ]] || {
    printf 'Token 为空或含换行，请重新输入。\n' >&2
    exit 1
  }
  printf '开始上传，会显示进度……\n'
  if ! printf 'DEPLOY-TOKEN: %s\n' "$MOM_UPLOAD_TOKEN" |
    curl --fail --show-error --progress-bar --connect-timeout 15 --max-time 1800 \
      --header @- --upload-file "$archive" --write-out '\nHTTP %{http_code}\n' \
      "https://bu17gitlab.luxshare.com.cn/api/v4/projects/80/packages/generic/mom-images/$release/$archive_name"; then
    printf '\n上传失败，重传：bash scripts/publish-images.sh --upload-only %s\n' "$release" >&2
    exit 1
  fi
  unset MOM_UPLOAD_TOKEN
fi

printf '\nGitLab 新建流水线时填写：\n'
printf 'MOM_COMPONENT=backend\nMOM_ACTION=deploy\nMOM_RELEASE=%s\nMOM_PACKAGE_SHA256=%s\n' "$release" "$digest"
if [[ $mode == package ]]; then
  printf '\n只打包完成。上传：bash scripts/publish-images.sh --upload-only %s\n' "$release"
else
  printf '\n上传完成。新建流水线，release_check 通过后手动点击 deploy_mom。\n'
fi
