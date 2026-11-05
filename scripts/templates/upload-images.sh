#!/usr/bin/env bash
# 发布包内独立运行；版本和外层镜像包摘要由打包流程写入。
set +x
set -euo pipefail
umask 077

trap 'unset MOM_UPLOAD_TOKEN' EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
trap 'exit 129' HUP

if [[ $# == 1 && $1 == --help ]]; then
  printf '用法：bash upload.sh\n将 upload.sh 和同版本镜像包放在同一目录，然后在可访问内网 GitLab 的机器上执行。\n'
  exit 0
fi
if [[ $# != 0 ]]; then
  printf '用法：bash upload.sh（版本和 SHA256 已包含在脚本中，无需参数）\n' >&2
  exit 1
fi

release='@MOM_RELEASE@'
expected_digest='@MOM_IMAGES_SHA256@'
[[ $release =~ ^[A-Za-z0-9][A-Za-z0-9_.-]{0,79}$ && $release != latest &&
   $expected_digest =~ ^[a-f0-9]{64}$ ]] || {
  printf '上传脚本的版本或 SHA256 无效，请使用发布包内生成的 upload.sh。\n' >&2
  exit 1
}

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
archive_name="mom-images-$release.tar.gz"
archive="$script_dir/$archive_name"
for tool in curl gzip tar; do
  command -v "$tool" >/dev/null || { printf '缺少工具：%s\n' "$tool" >&2; exit 1; }
done
[[ -f $archive && ! -L $archive && -s $archive ]] || {
  printf '找不到完整镜像包：%s\n请保留发布包的目录结构，不解压镜像 tar.gz。\n' "$archive" >&2
  exit 1
}
if command -v sha256sum >/dev/null; then
  digest=$(sha256sum "$archive")
elif command -v shasum >/dev/null; then
  digest=$(shasum -a 256 "$archive")
else
  printf '缺少 SHA256 工具：需要 sha256sum 或 shasum。\n' >&2
  exit 1
fi
digest=${digest%% *}
[[ $digest == "$expected_digest" ]] || {
  printf '镜像包 SHA256 与本次发布记录不一致，停止上传。请重新获取完整发布包。\n' >&2
  exit 1
}
gzip -t "$archive"
tar -tzf "$archive" "mom-images-$release/images.tar.gz" >/dev/null
printf '版本：%s\nSHA256：%s\n目标：内网 GitLab mom_img 项目的 Package Registry\n' "$release" "$digest"

if [[ -z ${MOM_UPLOAD_TOKEN:-} ]]; then
  if ! read -r -s -p '请输入 Package Registry 的 Deploy Token（粘贴后按回车）：' MOM_UPLOAD_TOKEN < /dev/tty; then
    printf '\n未读取到上传 Token。\n' >&2
    exit 1
  fi
  printf '\n'
fi
[[ -n $MOM_UPLOAD_TOKEN && $MOM_UPLOAD_TOKEN != *$'\n'* && $MOM_UPLOAD_TOKEN != *$'\r'* ]] || {
  printf 'Token 为空或含换行，请重新输入。\n' >&2
  exit 1
}

printf '校验通过，开始上传……\n'
if ! http_code=$(printf 'DEPLOY-TOKEN: %s\n' "$MOM_UPLOAD_TOKEN" |
  curl -q --fail --show-error --progress-bar --connect-timeout 15 --max-time 1800 \
    --proto '=https' --header @- --upload-file "$archive" \
    --output /dev/null --write-out '%{http_code}' \
    "https://bu17gitlab.luxshare.com.cn/api/v4/projects/80/packages/generic/mom-images/$release/$archive_name"); then
  printf '\n上传失败。发布包保留，排查后可重新执行 bash upload.sh。\n' >&2
  exit 1
fi
unset MOM_UPLOAD_TOKEN
if [[ $http_code != 201 ]]; then
  printf '\n上传未确认成功（HTTP %s），停止。请核对 GitLab 中该版本包的状态。\n' "$http_code" >&2
  exit 1
fi

printf '\n上传成功（HTTP 201）。在 mom_img 新建流水线时填写：\n'
printf 'MOM_COMPONENT=backend\nMOM_ACTION=deploy\nMOM_RELEASE=%s\nMOM_PACKAGE_SHA256=%s\n' "$release" "$digest"
printf '\n有数据库变更时，先得到 DBA 升级成功的确认，再手动运行 deploy_mom。\n'
