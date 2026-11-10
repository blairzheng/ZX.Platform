#!/usr/bin/env python3
"""输入正式库迁移 ID，在本机生成与 GitHub Actions 相同的后端发布包。"""

import argparse
from datetime import datetime, timezone
import importlib.util
import json
import os
from pathlib import Path
import shlex
import shutil
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[1]
API_PROJECT = 'Services/Identity/ZX.Platform.Identity.Api'


def run(command, *, capture=False):
    return subprocess.run(
        command, cwd=ROOT, check=True, text=True,
        stdout=subprocess.PIPE if capture else None,
    ).stdout


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--from-migration', help='正式库最后一条完整 MigrationId；省略时交互输入。')
    parser.add_argument('--release', help='发布版本；省略时使用 UTC 时间和当前提交自动生成。')
    parser.add_argument('--schema', default='ZX_IDENTITY', help='目标表 owner，默认 ZX_IDENTITY。')
    args = parser.parse_args()

    if sys.version_info < (3, 10):
        raise ValueError('本地打包需要 Python 3.10 或以上版本。')
    for tool in ('git', 'dotnet', 'docker', 'bash', 'tar', 'gzip', 'shasum'):
        if shutil.which(tool) is None:
            raise ValueError(f'缺少工具：{tool}。请先安装本机打包依赖。')

    branch = run(['git', 'branch', '--show-current'], capture=True).strip()
    if branch != 'team':
        raise ValueError('请先在 team 分支准备好本次发布代码，再运行本地打包。')
    commit = run(['git', 'rev-parse', 'HEAD'], capture=True).strip()
    release = args.release or f'{datetime.now(timezone.utc):%Y%m%d-local%H%M%S%f}-{commit[:12]}'
    from_migration = args.from_migration
    if from_migration is None:
        from_migration = input('请输入正式库最后一条完整 MigrationId：')
    from_migration = from_migration.strip()
    schema = args.schema.strip().upper()

    # Reuse the Actions packager's validation and output format.
    spec = importlib.util.spec_from_file_location('release_package', ROOT / 'scripts/package-release.py')
    package = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(package)
    package.validate_inputs(release, from_migration, schema)
    directory = ROOT / 'output/mom-release' / release
    image_archive = ROOT / 'output' / f'mom-images-{release}.tar.gz'
    if directory.exists() or image_archive.exists():
        raise ValueError('该版本输出已存在，请重新运行以生成新版本，或指定另一个 --release。')

    frontend = Path(os.environ.get('MOM_ASSISTANT_FRONTEND_DIR') or ROOT.parent / 'ZX.front.vue')
    if not frontend.is_absolute():
        frontend = ROOT / frontend
    frontend = frontend.resolve()
    if not (frontend / 'src').is_dir() or not (frontend / 'package.json').is_file():
        raise ValueError(f'找不到配套前端源码：{frontend}。可通过 MOM_ASSISTANT_FRONTEND_DIR 指定。')
    frontend_commit = run(['git', '-C', str(frontend), 'rev-parse', 'HEAD'], capture=True).strip()
    os.environ['MOM_ASSISTANT_FRONTEND_DIR'] = str(frontend)

    sdk = run(['dotnet', '--version'], capture=True).strip()
    if not sdk.startswith('10.0.'):
        raise ValueError(f'当前 .NET SDK 为 {sdk}；请使用 .NET 10.0 SDK，与 Actions 保持一致。')
    print('检查本机 Docker……', flush=True)
    run(['docker', 'info'], capture=True)
    run(['docker', 'buildx', 'version'], capture=True)

    print(
        f'\n发布版本：{release}\n后端提交：{commit}\n'
        f'AI 资料使用的前端提交：{frontend_commit}\n'
        f'正式库起点：{from_migration}\n目标 schema：{schema}\n'
        '使用本地工作区打包，未提交修改会记录在 release.json。\n'
        '仅离线导出 SQL，数据库执行仍交给 DBA。\n', flush=True)

    print('[1/4] 还原固定版本的 EF 工具', flush=True)
    run(['dotnet', 'tool', 'restore'])
    print('[2/4] 编译 Release 迁移与模型', flush=True)
    run(['dotnet', 'build', API_PROJECT, '--configuration', 'Release'])
    print('[3/4] 执行与 Actions 相同的离线检查', flush=True)
    for pattern in ('test_*sql_export.py', 'test_release_package.py', 'test_assistant_sources.py'):
        run([sys.executable, '-m', 'unittest', 'discover', '-s', 'deploy/docker/tests', '-p', pattern])
    print('[4/4] 生成镜像、数据库包和上传脚本', flush=True)
    run([
        sys.executable, 'scripts/package-release.py', '--release', release,
        '--from-migration', from_migration, '--schema', schema,
    ])

    metadata = json.loads((directory / 'release.json').read_text(encoding='utf-8'))
    database_state = ('需交 DBA 执行升级 SQL' if metadata['database']['requiresDatabaseUpgrade']
                      else '无数据库变更')
    print(
        f'\n本地打包完成：{directory}\n数据库：{database_state}\n'
        f'下一步上传镜像包：\n{shlex.join(["bash", str(directory / "upload.sh")])}\n'
        '上传成功后，按脚本输出的四项参数新建 GitLab 发布流水线。', flush=True)


if __name__ == '__main__':
    try:
        main()
    except (ValueError, OSError, subprocess.CalledProcessError, KeyError, EOFError) as error:
        print(f'本地打包失败：{error}', file=sys.stderr)
        sys.exit(1)
    except KeyboardInterrupt:
        print('\n已取消本地打包。', file=sys.stderr)
        sys.exit(130)
