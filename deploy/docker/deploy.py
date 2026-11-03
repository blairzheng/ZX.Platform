#!/usr/bin/env python3
"""Offline MOM container deployment. No database DDL, SSH, Nginx or Docker pruning."""
import argparse
import base64
import fcntl
import hashlib
import ipaddress
import json
import os
from pathlib import Path
import re
import signal
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid


NAMES = {'identity': 'mom-identity', 'gateway': 'mom-gateway'}
LABEL = 'cn.luxshare.mom.deployment'
NETWORK = 'mom-net'
DEFAULTS = {
    'subnet': '172.30.176.0/24', 'gateway_ip': '172.30.176.10',
    'identity_ip': '172.30.176.11', 'bind_ip': '0.0.0.0',
    'port': 18080, 'nginx_ip': '10.32.15.176', 'startup_timeout': 90,
}


class DeploymentError(Exception):
    pass


def atomic_json(path, data):
    temp = path.with_suffix('.tmp')
    with temp.open('w', encoding='utf-8') as stream:
        stream.write(json.dumps(data, indent=2) + '\n')
        stream.flush()
        os.fsync(stream.fileno())
    temp.chmod(0o600)
    os.replace(temp, path)
    directory = os.open(path.parent, os.O_RDONLY)
    try:
        os.fsync(directory)
    finally:
        os.close(directory)


def read_json(path):
    try:
        return json.loads(path.read_text(encoding='utf-8'))
    except (OSError, ValueError):
        raise DeploymentError(f'无法读取 JSON: {path.name}') from None


def run(args):
    # Never include command arguments or Docker stderr in errors: they may contain secrets.
    try:
        result = subprocess.run(args, text=True, capture_output=True)
    except OSError:
        raise DeploymentError(f'无法执行 {args[0]}，请确认命令已安装且当前用户有权限。') from None
    if result.returncode:
        raise DeploymentError(f'{args[0]} {args[1]} 执行失败，请检查服务状态。')
    return result.stdout


def docker(*args):
    return run(['docker', *args])


def inspect(ref):
    try:
        result = subprocess.run(['docker', 'container', 'inspect', ref], text=True, capture_output=True)
    except OSError:
        raise DeploymentError('无法执行 docker，请确认已安装 Docker Engine。') from None
    if result.returncode:
        return None
    return json.loads(result.stdout)[0]


def read_env(path):
    try:
        lines = path.read_text(encoding='utf-8').splitlines()
    except OSError:
        raise DeploymentError(f'缺少配置 {path}；首次执行 deploy.sh init。') from None
    values = {}
    for number, raw in enumerate(lines, 1):
        if not raw.strip() or raw.lstrip().startswith('#'):
            continue
        key, separator, value = raw.partition('=')
        if not separator or not re.fullmatch(r'[A-Za-z_][A-Za-z0-9_.]*', key) or key in values:
            raise DeploymentError(f'{path.name} 第 {number} 行格式错误或键重复。')
        if not value or value[0] in '\"\'' or value != value.strip():
            raise DeploymentError(f'{path.name} 第 {number} 行值为空、带引号或含首尾空格。')
        if 'CHANGE_ME' in value or '<FILL_ON_SERVER>' in value or '填在' in value or '自行设置' in value:
            raise DeploymentError(f'{path.name} 的 {key} 尚未填写。')
        values[key] = value
    return values


def settings_from(path):
    data = read_json(path)
    if not isinstance(data, dict) or set(data) != set(DEFAULTS):
        raise DeploymentError('deploy.json 配置项与模板不一致。')
    try:
        network = ipaddress.IPv4Network(data['subnet'])
        addresses = [ipaddress.IPv4Address(data[key]) for key in ('gateway_ip', 'identity_ip')]
        if network.prefixlen > 28 or len(set(addresses)) != 2:
            raise ValueError()
        if any(ip not in network or ip in (network.network_address, network.broadcast_address,
                                          network.network_address + 1) for ip in addresses):
            raise ValueError()
        ipaddress.IPv4Address(data['bind_ip'])
        ipaddress.IPv4Address(data['nginx_ip'])
        if type(data['port']) is not int or not 1024 <= data['port'] <= 65535:
            raise ValueError()
        if type(data['startup_timeout']) is not int or not 10 <= data['startup_timeout'] <= 600:
            raise ValueError()
    except (ValueError, TypeError, KeyError):
        raise DeploymentError('deploy.json 的网段、IP、端口或等待时间无效。') from None
    return data


def transport(service, settings):
    values = {
        'DOTNET_ENVIRONMENT': 'Production', 'ASPNETCORE_ENVIRONMENT': 'Production',
        'ASPNETCORE_URLS': 'http://0.0.0.0:8080',
        'ForwardedHeaders__KnownProxies__0': settings['gateway_ip'] if service == 'identity' else settings['nginx_ip'],
        'ForwardedHeaders__KnownProxies__1': '::1',
    }
    if service == 'gateway':
        values['ReverseProxy__Clusters__identity-cluster__Destinations__identity-api__Address'] = 'http://mom-identity:8080/'
    return values


def validate_env(config, settings):
    for service in NAMES:
        path = config / f'{service}.env'
        values = read_env(path)
        for key, value in transport(service, settings).items():
            if key in values and values[key] != value:
                raise DeploymentError(f'{path.name} 的 {key} 与 deploy.json 或部署模式不一致。')
        if any(key.startswith('ForwardedHeaders__KnownProxies__') and key not in transport(service, settings)
               for key in values):
            raise DeploymentError(f'{path.name} 包含额外可信代理，请使用 deploy.json 管理。')
        if service == 'gateway':
            continue
        cookie_secure = values.get('AuthenticationCookie__Secure', 'true')
        allow_http = values.get('AuthenticationCookie__AllowInsecureHttp', 'false')
        if cookie_secure not in ('true', 'false') or allow_http not in ('true', 'false'):
            raise DeploymentError('AuthenticationCookie 的 Secure、AllowInsecureHttp 必须是 true 或 false。')
        if cookie_secure == 'false' and allow_http != 'true':
            raise DeploymentError('HTTP 测试需同时设置 AuthenticationCookie__Secure=false 和 AuthenticationCookie__AllowInsecureHttp=true。')
        required = ['IdentityDatabase__Schema', 'ConnectionStrings__IdentityDatabase',
                    'ConnectionStrings__IdentityRedis', 'AuthenticationRedis__KeyPrefix',
                    'AuthenticationJwt__SigningKey', 'AuthenticationJwt__Issuer', 'AuthenticationJwt__Audience']
        for key in required:
            if not values.get(key):
                raise DeploymentError(f'identity.env 缺少 {key}。')
        if not re.fullmatch(r'[A-Z][A-Z0-9_]{0,127}', values['IdentityDatabase__Schema']):
            raise DeploymentError('IdentityDatabase__Schema 必须是大写 Oracle 标识符。')
        if 'servicename=' not in values['ConnectionStrings__IdentityRedis'].lower():
            raise DeploymentError('Redis 连接必须包含 Sentinel serviceName。')
        try:
            if len(base64.b64decode(values['AuthenticationJwt__SigningKey'], validate=True)) < 32:
                raise ValueError()
        except ValueError:
            raise DeploymentError('JWT SigningKey 必须是至少 32 字节的 Base64 密钥。') from None
def bundle_manifest(bundle):
    sums = bundle / 'SHA256SUMS'
    try:
        entries = sums.read_text().splitlines()
    except OSError:
        raise DeploymentError('找不到离线包校验文件；请先运行 package.sh 并解压发布包。') from None
    checked = set()
    for entry in entries:
        match = re.fullmatch(r'([a-f0-9]{64})  ([A-Za-z0-9_./-]+)', entry)
        if not match:
            raise DeploymentError('离线包校验文件格式无效。')
        digest, name = match.groups()
        path = bundle / name
        if Path(name).is_absolute() or '..' in Path(name).parts or name in checked or path.is_symlink():
            raise DeploymentError('离线包包含无效路径。')
        hashed = hashlib.sha256()
        try:
            with path.open('rb') as source:
                for block in iter(lambda: source.read(1024 * 1024), b''):
                    hashed.update(block)
        except OSError:
            raise DeploymentError(f'离线包缺少 {name}。') from None
        if hashed.hexdigest() != digest:
            raise DeploymentError(f'{name} 校验失败，停止部署，请重新上传。')
        checked.add(name)
    required = {'manifest.json', 'images.tar', 'deploy.sh', 'deploy.py', 'README.md',
                'templates/deploy.json.example', 'templates/identity.env.example', 'templates/gateway.env.example'}
    if checked != required:
        raise DeploymentError('离线包文件清单与当前格式不一致。')
    manifest = read_json(bundle / 'manifest.json')
    release = manifest.get('release', '')
    if manifest.get('format') != 1 or not re.fullmatch(r'[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}', release) or release == 'latest':
        raise DeploymentError('发布版本号无效。')
    for service in NAMES:
        image = manifest.get('images', {}).get(service, {})
        if image.get('tag') != f'mom-{service}:{release}' or not re.fullmatch(r'sha256:[0-9a-f]{64}', image.get('id', '')):
            raise DeploymentError('发布镜像标识无效。')
    return manifest


def owned(info, service):
    return (info['Config'].get('Labels') or {}).get(LABEL) == '1' or info['Config']['Image'].startswith(f'mom-{service}:')


def network_check(settings):
    ids = docker('network', 'ls', '-q').split()
    networks = json.loads(docker('network', 'inspect', *ids)) if ids else []
    wanted = ipaddress.ip_network(settings['subnet'])
    existing = None
    for network in networks:
        subnets = [item.get('Subnet') for item in network.get('IPAM', {}).get('Config') or [] if item.get('Subnet')]
        if network['Name'] == NETWORK:
            if network['Driver'] != 'bridge' or subnets != [settings['subnet']]:
                raise DeploymentError('已有 mom-net 的驱动或网段不同，请核对配置，不要删除运行中的网络。')
            existing = network
        elif any(ipaddress.ip_network(subnet).version == 4 and wanted.overlaps(ipaddress.ip_network(subnet)) for subnet in subnets):
            raise DeploymentError(f'部署网段与已有 Docker 网络 {network["Name"]} 重叠。')
    routes = json.loads(run(['ip', '-j', '-4', 'route', 'show']))
    for route in routes:
        destination = route.get('dst', 'default')
        if destination == 'default':
            continue
        bridge = (existing or {}).get('Options', {}).get('com.docker.network.bridge.name')
        bridge = bridge or ('br-' + existing['Id'][:12] if existing else None)
        if bridge and route.get('dev') == bridge:
            continue
        if wanted.overlaps(ipaddress.ip_network(destination, strict=False)):
            raise DeploymentError('部署网段与服务器已有路由重叠，请修改 deploy.json。')
    if existing:
        for endpoint in existing.get('Containers', {}).values():
            ip = endpoint.get('IPv4Address', '').split('/')[0]
            if ip in (settings['gateway_ip'], settings['identity_ip']) and endpoint['Name'] not in NAMES.values():
                raise DeploymentError('计划使用的容器固定 IP 已被其他容器占用。')
    return existing


def port_check(settings):
    current = inspect(NAMES['gateway'])
    if current and current['State']['Running']:
        ports = current['HostConfig'].get('PortBindings', {}).get('8080/tcp') or []
        if any(item['HostPort'] == str(settings['port']) and (item['HostIp'] or '0.0.0.0') == settings['bind_ip'] for item in ports):
            return
    try:
        with socket.socket() as probe:
            probe.bind((settings['bind_ip'], settings['port']))
    except OSError:
        raise DeploymentError('Gateway 宿主机端口已被占用或绑定地址不可用。') from None


def ready(settings):
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, *_args, **_kwargs):
            return None
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
    host = settings['bind_ip'] if settings['bind_ip'] != '0.0.0.0' else '127.0.0.1'
    url = f'http://{host}:{settings["port"]}/api/identity/api/Auth/me'
    try:
        opener.open(url, timeout=3).close()
    except urllib.error.HTTPError as response:
        try:
            return response.code == 401 and json.loads(response.read(65536)).get('code') == 600401
        except (ValueError, OSError):
            return False
        finally:
            response.close()
    except (OSError, urllib.error.URLError):
        pass
    return False


def wait_ready(settings, expected_restarts=None):
    expected_restarts = expected_restarts or {}
    end = time.monotonic() + settings['startup_timeout']
    successes = 0
    while time.monotonic() < end:
        for name in NAMES.values():
            info = inspect(name)
            if not info or not info['State']['Running'] or info.get('RestartCount', 0) != expected_restarts.get(name, 0):
                raise DeploymentError(f'{name} 未保持正常运行，请查看该容器日志。')
        successes = successes + 1 if ready(settings) else 0
        if successes >= 3:
            return
        time.sleep(2)
    raise DeploymentError('等待 Identity 认证接口就绪超时，请查看 MOM 容器日志。')


class Deployment:
    def __init__(self, bundle, config):
        self.bundle, self.config = bundle, config
        self.state_file = config / 'state.json'
        self.pending_file = config / 'transaction.json'

    def init(self):
        self.config.mkdir(parents=True, exist_ok=True, mode=0o700)
        self.config.chmod(0o700)
        for name in ('identity.env', 'gateway.env', 'deploy.json'):
            target = self.config / name
            if target.exists():
                print(f'保留已有配置: {target}')
                continue
            source = self.bundle / 'templates' / (name + '.example')
            with target.open('x', encoding='utf-8') as stream:
                stream.write(source.read_text(encoding='utf-8'))
            target.chmod(0o600)
            print(f'已创建: {target}')
        print('填写 identity.env 中的数据库、Redis、JWT 配置；两台共享值必须一致。')

    def state(self):
        return read_json(self.state_file) if self.state_file.exists() else {}

    def preflight(self):
        if self.pending_file.exists():
            raise DeploymentError('存在未完成的部署，请先执行 deploy.sh recover。')
        settings = settings_from(self.config / 'deploy.json')
        validate_env(self.config, settings)
        docker('info')
        for service, name in NAMES.items():
            info = inspect(name)
            if info and not owned(info, service):
                raise DeploymentError(f'{name} 不是 MOM 镜像，停止操作。')
        network = network_check(settings)
        port_check(settings)
        return settings, network

    def image_check(self, images):
        for service, image in images.items():
            info = json.loads(docker('image', 'inspect', image['id']))[0]
            if info['Id'] != image['id'] or (info['Os'], info['Architecture']) != ('linux', 'amd64'):
                raise DeploymentError(f'{service} 镜像 ID 或架构不符。')

    def create(self, service, image_id, settings, transaction):
        args = ['run', '-d', '--pull', 'never', '--name', NAMES[service],
                '--label', f'{LABEL}=1', '--label', f'{LABEL}.transaction={transaction}',
                '--restart', 'unless-stopped', '--stop-timeout', '30', '--network', NETWORK,
                '--ip', settings[service + '_ip'], '--env-file', str(self.config / f'{service}.env'),
                '--log-driver', 'json-file', '--log-opt', 'max-size=50m', '--log-opt', 'max-file=5']
        for key, value in transport(service, settings).items():
            args.extend(['--env', f'{key}={value}'])
        if service == 'gateway':
            args.extend(['--publish', f'{settings["bind_ip"]}:{settings["port"]}:8080'])
        docker(*args, image_id)

    def disconnect(self, container_id):
        info = inspect(container_id)
        if NETWORK in info['NetworkSettings']['Networks']:
            # Stopped backups must release their endpoint/IP before the replacement starts.
            docker('network', 'disconnect', '--force', NETWORK, container_id)

    def reconnect(self, container):
        endpoint = container.get('endpoint')
        if endpoint is None or NETWORK in inspect(container['id'])['NetworkSettings']['Networks']:
            return
        args = ['network', 'connect']
        address = endpoint.get('IPAddress') or (endpoint.get('IPAMConfig') or {}).get('IPv4Address')
        if address:
            args.extend(['--ip', address])
        for alias in endpoint.get('Aliases') or []:
            args.extend(['--alias', alias])
        docker(*args, NETWORK, container['id'])

    def recover(self):
        if not self.pending_file.exists():
            print('没有未完成事务。')
            return
        transaction = read_json(self.pending_file)
        if self.state().get('transaction') == transaction['id']:
            self.pending_file.unlink()
            print('上次部署已提交，已清除事务标记。')
            return
        # Remove only containers created by this transaction, or return rollback targets to backup names.
        if transaction['kind'] == 'deploy':
            for name in reversed(list(NAMES.values())):
                info = inspect(name)
                if info and (info['Config'].get('Labels') or {}).get(f'{LABEL}.transaction') == transaction['id']:
                    docker('rm', '-f', info['Id'])
        else:
            for target in reversed(transaction['targets']):
                info = inspect(target['id'])
                if info and info['Name'].lstrip('/') == target['name']:
                    docker('stop', '--time', '30', target['id'])
                    self.disconnect(target['id'])
                    docker('rename', target['id'], target['backup'])
        for original in transaction['originals']:
            info = inspect(original['id'])
            if not info:
                raise DeploymentError('原容器已被外部删除，无法自动恢复；保留事务记录。')
            if info['Name'].lstrip('/') != original['name']:
                docker('rename', original['id'], original['name'])
            self.reconnect(original)
            if original['running']:
                docker('start', original['id'])
        self.pending_file.unlink()
        print('已恢复部署前的容器和运行状态；数据库及配置文件未回退。')

    def replace(self, images, release, settings, targets=None):
        previous_state = self.state()
        transaction_id = uuid.uuid4().hex
        originals = []
        for service, name in NAMES.items():
            info = inspect(name)
            if info:
                originals.append({'id': info['Id'], 'name': name, 'running': info['State']['Running'],
                                  'backup': name + '.previous-' + transaction_id[:8],
                                  'endpoint': info['NetworkSettings']['Networks'].get(NETWORK),
                                  'restarts': info.get('RestartCount', 0)})
        transaction = {'id': transaction_id, 'kind': 'rollback' if targets else 'deploy',
                       'originals': originals, 'targets': targets or []}
        atomic_json(self.pending_file, transaction)
        try:
            for original in reversed(originals):
                docker('stop', '--time', '30', original['id'])
                self.disconnect(original['id'])
                docker('rename', original['id'], original['backup'])
            if targets:
                for target in targets:
                    docker('rename', target['id'], target['name'])
                    self.reconnect(target)
                    docker('start', target['id'])
            else:
                for service in NAMES:
                    self.create(service, images[service]['id'], settings, transaction_id)
            wait_ready(settings, {item['name']: item.get('restarts', 0) for item in targets or []})
            atomic_json(self.state_file, {'release': release, 'settings': settings, 'images': images,
                                         'transaction': transaction_id, 'previous': originals,
                                         'previous_release': previous_state.get('release', 'manual'),
                                         'previous_settings': previous_state.get('settings', settings),
                                         'previous_images': previous_state.get('images', {})})
        except (Exception, KeyboardInterrupt):
            print('新版本未通过启动检查，正在恢复原容器。', file=sys.stderr)
            self.recover()
            raise
        self.pending_file.unlink()
        # Keep exactly the preceding generation, never prune unrelated Docker resources.
        active_ids = {inspect(name)['Id'] for name in NAMES.values()}
        for item in previous_state.get('previous', []):
            if item['id'] not in active_ids and inspect(item['id']):
                try:
                    docker('rm', item['id'])
                except DeploymentError:
                    print('旧备份容器未清除，可稍后检查；本次部署已成功。', file=sys.stderr)
        print(f'版本 {release} 已启动，认证接口连续三次返回预期 401。请从 176 的 HTTPS 入口验收登录。')

    def deploy(self, apply_config=False, check_only=False):
        settings, network = self.preflight()
        if apply_config:
            images = {}
            for service, name in NAMES.items():
                info = inspect(name)
                if not info:
                    raise DeploymentError('apply-config 需要两个现有 MOM 容器；首次部署请直接执行 deploy.sh。')
                images[service] = {'id': info['Image']}
            release = self.state().get('release', 'manual')
        else:
            print('正在校验离线包……')
            manifest = bundle_manifest(self.bundle)
            images, release = manifest['images'], manifest['release']
        if check_only:
            print('配置、网段、端口和离线包检查通过；未导入镜像、未替换容器，也未检查数据库迁移版本。')
            return
        if not apply_config:
            print('正在导入离线镜像……')
            docker('load', '--input', str(self.bundle / 'images.tar'))
        self.image_check(images)
        if not network:
            docker('network', 'create', '--subnet', settings['subnet'], NETWORK)
        self.replace(images, release, settings)

    def rollback(self):
        if self.pending_file.exists():
            raise DeploymentError('存在未完成事务，请先执行 deploy.sh recover。')
        state = self.state()
        targets = state.get('previous', [])
        if len(targets) != 2 or any(not inspect(target['id']) for target in targets):
            raise DeploymentError('没有完整的上一版本容器；保持现状。')
        for service, name in NAMES.items():
            info = inspect(name)
            if not info or not owned(info, service):
                raise DeploymentError('当前 MOM 容器不完整，无法执行标准回滚。')
        settings = state['previous_settings']
        self.replace(state.get('previous_images', {}), state['previous_release'], settings, targets)

    def status(self):
        docker('info')
        state = self.state()
        print(f'当前版本: {state.get("release", "未记录（或手工部署）")}')
        print(f'上一版本: {state.get("previous_release", "无")}')
        if self.pending_file.exists():
            print('存在未完成事务，请执行 recover。')
        for name in NAMES.values():
            info = inspect(name)
            print(f'{name}: ' + (f'{info["State"]["Status"]} restart={info.get("RestartCount", 0)}' if info else '不存在'))


def main():
    parser = argparse.ArgumentParser(description='MOM 离线部署；默认部署当前离线包。先在 Nginx 摘除当前节点，DBA 先执行所需 SQL。')
    parser.add_argument('command', nargs='?', default='deploy',
                        choices=['init', 'check', 'deploy', 'apply-config', 'status', 'rollback', 'recover'])
    parser.add_argument('--config-dir', type=Path, default=Path('/etc/mom'))
    args = parser.parse_args()
    if sys.platform != 'linux' or os.geteuid() != 0:
        raise DeploymentError('请在目标 Linux 服务器通过 sudo bash deploy.sh 执行。')
    os.umask(0o077)
    config = args.config_dir.resolve()
    config.mkdir(parents=True, exist_ok=True, mode=0o700)
    app = Deployment(Path(__file__).resolve().parent, config)
    with (config / '.deploy.lock').open('a') as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise DeploymentError('另一个部署命令正在运行。') from None
        if args.command in ('init', 'status', 'rollback', 'recover'):
            getattr(app, args.command)()
        else:
            app.deploy(apply_config=args.command == 'apply-config', check_only=args.command == 'check')


if __name__ == '__main__':
    def interrupted(_signal, _frame):
        raise DeploymentError('部署被中断。')
    signal.signal(signal.SIGTERM, interrupted)
    try:
        main()
    except (DeploymentError, KeyboardInterrupt) as error:
        print(f'部署未完成: {error}', file=sys.stderr)
        sys.exit(1)
