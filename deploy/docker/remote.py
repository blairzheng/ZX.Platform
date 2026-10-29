#!/usr/bin/env python3
"""Parallel deployment through system ssh/sftp and Python's standard library only."""
import argparse
import base64
import codecs
from concurrent.futures import ThreadPoolExecutor, as_completed
from dataclasses import dataclass, field
import getpass
import hashlib
import json
import os
from pathlib import Path
import re
import select
import shlex
import shutil
import subprocess
import sys
import tarfile
import tempfile
import termios
import threading
import time
import uuid
import warnings


CONSOLE = threading.Lock()
FILES = {'manifest.json', 'images.tar', 'SHA256SUMS', 'deploy.sh', 'deploy.py', 'README.md',
         'templates/identity.env.example', 'templates/gateway.env.example', 'templates/deploy.json.example'}


class RemoteError(Exception):
    pass


class ProcessChannel:
    """A system ssh process attached to its own terminal, including password prompts."""
    def __init__(self, command):
        self.fd, slave = os.openpty()
        settings = termios.tcgetattr(slave)
        settings[3] &= ~termios.ECHO
        termios.tcsetattr(slave, termios.TCSANOW, settings)
        # Set the controlling terminal in a child interpreter, never preexec_fn in threads.
        child = ('import fcntl,os,sys,termios; os.setsid(); '
                 'fcntl.ioctl(0,termios.TIOCSCTTY,0); os.execvp(sys.argv[1],sys.argv[1:])')
        try:
            self.process = subprocess.Popen([sys.executable, '-c', child, *command],
                                            stdin=slave, stdout=slave, stderr=slave, close_fds=True)
        except BaseException:
            os.close(self.fd)
            raise
        finally:
            os.close(slave)
        self.closed = False

    def settimeout(self, _timeout):
        pass

    def sendall(self, value):
        remaining = value.encode()
        while remaining:
            count = os.write(self.fd, remaining)
            remaining = remaining[count:]

    def recv_ready(self):
        return not self.closed and bool(select.select([self.fd], [], [], 0)[0])

    def recv(self, count):
        try:
            return os.read(self.fd, count)
        except OSError:
            return b''

    def exit_status_ready(self):
        return self.process.poll() is not None

    def close(self):
        if self.closed:
            return
        if self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait()
        os.close(self.fd)
        self.closed = True


class SystemSSH:
    """System OpenSSH owns authentication and encryption; sftp reuses its socket."""
    def __init__(self, credentials, node):
        self.credentials, self.node = credentials, node
        self.directory = tempfile.TemporaryDirectory(prefix='mom-ssh-', dir='/tmp')
        self.socket = Path(self.directory.name) / 'control'
        self.channel = None

    def connect(self):
        item = self.credentials
        command = ['ssh', '-tt', '-M', '-S', str(self.socket),
                   '-o', 'ControlPersist=no', '-o', 'StrictHostKeyChecking=ask',
                   '-o', 'ConnectTimeout=20', '-o', 'NumberOfPasswordPrompts=1',
                   '-o', 'ServerAliveInterval=20', '-o', 'ServerAliveCountMax=3',
                   '-p', str(item.port), f'{item.user}@{item.host}']
        self.channel = ProcessChannel(command)
        self.authenticate()

    def authenticate(self):
        buffer, sent_password = '', False
        decoder = codecs.getincrementaldecoder('utf-8')('replace')
        deadline = time.monotonic() + 60
        while time.monotonic() < deadline:
            if self.channel.recv_ready():
                chunk = self.channel.recv(65536)
                if not chunk:
                    break
                buffer += decoder.decode(chunk)
                if 'are you sure you want to continue connecting' in buffer.lower():
                    with CONSOLE:
                        print(self.node.redact(buffer), flush=True)
                        answer = input(f'[{self.credentials.label}] 核对以上主机指纹后输入 yes 信任: ')
                    if answer.strip() != 'yes':
                        raise RemoteError('未确认主机指纹，已停止。')
                    self.channel.sendall('yes\n')
                    buffer = ''
                if re.search(r'(?:password|密码)[^:\r\n]{0,120}:\s*$', buffer, re.I):
                    if sent_password:
                        raise RemoteError('临时密码被拒绝，请重新生成该节点凭据。')
                    self.channel.sendall(self.credentials.password + '\n')
                    sent_password = True
                    buffer = ''
            # The local control socket is created only after SSH authenticates.
            if self.socket.exists():
                return
            if self.channel.exit_status_ready():
                break
            buffer = buffer[-8192:]
            time.sleep(0.05)
        detail = self.node.redact(buffer.strip()) or '没有收到 SSH 登录结果。'
        raise RemoteError('系统 SSH 登录失败或超时: ' + detail)

    def sftp(self, commands, timeout=1800):
        if not self.socket.exists() or self.channel.exit_status_ready():
            raise RemoteError('SSH 连接已断开；停止传输，请申请新凭据。')
        item = self.credentials
        command = ['sftp', '-b', '-', '-o', 'BatchMode=yes', '-o', 'ControlMaster=no',
                   '-o', 'ControlPath=' + str(self.socket), '-o', 'ProxyCommand=false',
                   '-P', str(item.port), f'{item.user}@{item.host}']
        # ProxyCommand=false forbids a fresh connection if the shared socket disappears.
        try:
            result = subprocess.run(command, input='\n'.join(commands) + '\n',
                                    text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                    timeout=timeout)
        except subprocess.TimeoutExpired:
            raise RemoteError('系统 SFTP 传输超时，本阶段未完成。') from None
        if result.returncode:
            detail = self.node.redact(result.stdout.strip())[-4000:]
            raise RemoteError(f'系统 SFTP 返回 {result.returncode}: {detail}')

    def close(self):
        try:
            if self.channel:
                self.channel.close()
        finally:
            self.directory.cleanup()


def sftp_quote(path):
    text = str(path)
    if any(char in text for char in '\n\r\0'):
        raise RemoteError('文件路径不能包含换行或空字符。')
    return '"' + text.replace('\\', '\\\\').replace('"', '\\"') + '"'


@dataclass
class Credentials:
    label: str
    host: str
    port: int
    user: str
    password: str = field(repr=False)
    sudo_password: str = field(repr=False)


def parse_ssh(text):
    try:
        words = shlex.split(text.strip())
    except ValueError:
        raise RemoteError('SSH 命令格式错误，密码请在下一步单独输入。') from None
    if not words or words.pop(0) != 'ssh':
        raise RemoteError('请粘贴 ssh 用户@主机 -p 端口，不包含密码。')
    destination, port = None, 22
    while words:
        word = words.pop(0)
        if word == '-p' and words:
            try:
                port = int(words.pop(0))
            except ValueError:
                raise RemoteError('SSH 端口无效。') from None
        elif not word.startswith('-') and destination is None:
            destination = word
        else:
            raise RemoteError('只支持 ssh 用户@主机 -p 端口；不要附加密码、命令或其他参数。')
    match = re.fullmatch(r'([A-Za-z0-9_.-]+)@([A-Za-z0-9][A-Za-z0-9.-]*)', destination or '')
    if not match or not 1 <= port <= 65535:
        raise RemoteError('SSH 账号、主机或端口无效，密码请单独输入。')
    return match.group(1), match.group(2), port


def collect_credentials():
    if not sys.stdin.isatty():
        raise RemoteError('请在交互终端运行，脚本需要隐藏输入临时密码。')
    credentials = []
    for label in ('177', '178'):
        user, host, port = parse_ssh(input(f'{label} 临时 SSH 命令（不含密码）: '))
        with warnings.catch_warnings():
            warnings.simplefilter('error', getpass.GetPassWarning)
            password = getpass.getpass(f'{label} 临时 SSH 密码: ')
            sudo_password = getpass.getpass(f'{label} sudo 密码（免密直接回车；与 SSH 相同输入 =）: ')
        if not password:
            raise RemoteError(f'{label} 的临时密码不能为空。')
        credentials.append(Credentials(label, host, port, user, password,
                                       password if sudo_password == '=' else sudo_password))
    if len({(item.host, item.port, item.user) for item in credentials}) != 2:
        raise RemoteError('177、178 需要各自的临时账号，不能两台使用同一个连接账号。')
    return credentials


def package_info(archive):
    if not archive or not archive.is_file():
        raise RemoteError('请提供 package.sh 生成的 .tar.gz 文件。')
    match = re.fullmatch(r'mom-backend-([A-Za-z0-9_][A-Za-z0-9_.-]{0,127})\.tar\.gz', archive.name)
    if not match:
        raise RemoteError('离线包文件名格式不符，请使用 package.sh 生成的原始文件名。')
    root = archive.name[:-7]
    expected = {root, root + '/templates'} | {root + '/' + name for name in FILES}
    try:
        with tarfile.open(archive, 'r:gz') as source:
            members = source.getmembers()
            if {item.name.rstrip('/') for item in members} != expected or len(members) != len(expected):
                raise RemoteError('离线包文件清单不符合预期。')
            for item in members:
                if not (item.isfile() or item.isdir()) or item.name.startswith('/') or '..' in Path(item.name).parts:
                    raise RemoteError('离线包存在不安全的路径或链接。')
            member = source.getmember(root + '/manifest.json')
            if member.size > 65536:
                raise RemoteError('版本清单过大。')
            manifest = json.load(source.extractfile(member))
            if manifest.get('release') != match.group(1) or manifest.get('format') != 1:
                raise RemoteError('包内版本与文件名不一致。')
    except (OSError, ValueError, tarfile.TarError, KeyError):
        raise RemoteError('无法读取有效离线包，请重新打包。') from None
    digest = hashlib.sha256()
    with archive.open('rb') as source:
        for block in iter(lambda: source.read(1024 * 1024), b''):
            digest.update(block)
    return root, digest.hexdigest()


def python_command(source, arguments, root=False):
    # Only a static Python program and base64 JSON enter the remote shell command.
    encoded = base64.b64encode(json.dumps(arguments).encode()).decode()
    code = 'import base64,json,sys\np=json.loads(base64.b64decode(sys.argv[1]))\n' + source
    return ('sudo -n -- ' if root else '') + 'python3 -u -c ' + shlex.quote(code) + ' ' + shlex.quote(encoded)


IDENTITY = '''
import platform,subprocess
addresses=subprocess.check_output(['hostname','-I'],text=True).split()
if p['expected_ip'] not in addresses:
    raise SystemExit('目标服务器 IP 不匹配；停止部署。')
print('MOM_DATA:'+json.dumps({'hostname':platform.node(),'ip':p['expected_ip']}))
'''

PROBE = '''
import hashlib,os
from pathlib import Path
path=Path(p['path'])
if hashlib.sha256(path.read_bytes()).hexdigest()!=p['sha256']:
    raise SystemExit('SFTP 与命令会话的文件内容或目标路径不一致。')
path.unlink()
print('命令会话已核对并删除小文件；文件传输确实到达目标服务器。')
'''

CLEANUP = '''
import re,shutil
if not re.fullmatch(r'/tmp/mom-remote-[0-9a-f]{32}',p['path']):
    raise SystemExit('清理路径不符。')
shutil.rmtree(p['path'],ignore_errors=True)
'''

EXTRACT = '''
import hashlib,shutil,tarfile
from pathlib import Path
archive=Path(p['archive'])
digest=hashlib.sha256()
with archive.open('rb') as stream:
    for block in iter(lambda:stream.read(1024*1024),b''):
        digest.update(block)
if digest.hexdigest()!=p['sha256']:
    raise SystemExit('离线压缩包 SHA256 不一致，停止。')
root=archive.parent
expected=set(p['members'])
seen=set()
with tarfile.open(archive,'r:gz') as source:
    for member in source:
        name=member.name.rstrip('/')
        if name not in expected or name in seen or not (member.isfile() or member.isdir()):
            raise SystemExit('压缩包文件清单无效。')
        seen.add(name)
        target=root/name
        if member.isdir():
            target.mkdir(mode=0o700,parents=True,exist_ok=True)
        else:
            target.parent.mkdir(mode=0o700,parents=True,exist_ok=True)
            with source.extractfile(member) as incoming,target.open('xb') as output:
                shutil.copyfileobj(incoming,output,1024*1024)
            target.chmod(0o600)
if seen!=expected:
    raise SystemExit('离线包文件不完整。')
print('镜像包 SHA256 核对成功，已解压。')
'''

ROOT_ACTION = '''
import subprocess
raise SystemExit(subprocess.call(['bash',p['script'],p['action']]))
'''

SHARED = '''
import hashlib,hmac,importlib.util
from pathlib import Path
spec=importlib.util.spec_from_file_location('mom_deploy',p['module'])
module=importlib.util.module_from_spec(spec)
sys.dont_write_bytecode=True
spec.loader.exec_module(module)
values=module.read_env(Path('/etc/mom/identity.env'))
keys=['IdentityDatabase__Schema','ConnectionStrings__IdentityDatabase','ConnectionStrings__IdentityRedis',
      'AuthenticationRedis__KeyPrefix','AuthenticationJwt__SigningKey',
      'AuthenticationJwt__Issuer','AuthenticationJwt__Audience']
shared=[values[key] for key in keys]
shared += [values.get('AuthenticationCookie__Secure','true'),
           values.get('AuthenticationCookie__AllowInsecureHttp','false')]
data=json.dumps(shared,ensure_ascii=False).encode()
fingerprint=hmac.new(bytes.fromhex(p['challenge']),data,hashlib.sha256).hexdigest()
print('MOM_DATA:'+json.dumps({'fingerprint':fingerprint}))
'''


class Shell:
    def __init__(self, channel, node):
        self.channel, self.node = channel, node
        self.channel.settimeout(0.2)

    def run(self, command, timeout=120, check=True, display=True, sudo_prompt=None):
        token = uuid.uuid4().hex
        begin, end = '__MOM_BEGIN_' + token + '__', '__MOM_END_' + token + '__'
        wrapper = "printf '\\n%s\\n' " + shlex.quote(begin) + '; ( ' + command + ' ); '
        wrapper += "_mom_rc=$?; printf '\\n%s:%s\\n' " + shlex.quote(end) + ' "$_mom_rc"\n'
        self.channel.sendall(wrapper)
        decoder = codecs.getincrementaldecoder('utf-8')('replace')
        buffer, output, begun, sent_password = '', [], False, False
        deadline = time.monotonic() + timeout
        begin_re = re.compile(r'(?:^|\n)' + re.escape(begin) + r'\r?\n')
        end_re = re.compile(r'(?:^|\n)' + re.escape(end) + r':([0-9]+)\r?\n')
        while time.monotonic() < deadline:
            if self.channel.recv_ready():
                chunk = self.channel.recv(65536)
                if not chunk:
                    break
                buffer += decoder.decode(chunk)
            elif self.channel.closed or self.channel.exit_status_ready():
                break
            else:
                time.sleep(0.05)
                continue
            if not begun:
                start = begin_re.search(buffer)
                if not start:
                    buffer = buffer[-65536:]
                    continue
                buffer, begun = buffer[start.end():], True
            if sudo_prompt and sudo_prompt in buffer:
                if sent_password or not self.node.credentials.sudo_password:
                    self.channel.sendall('\x03')
                    raise RemoteError('sudo 需要正确密码；请重新运行并填写该服务器的 sudo 密码。')
                self.channel.sendall(self.node.credentials.sudo_password + '\n')
                buffer = buffer.replace(sudo_prompt, '')
                sent_password = True
            finish = end_re.search(buffer)
            content = buffer[:finish.start()] if finish else buffer
            if finish:
                lines = content.splitlines()
                output.extend(lines)
                if display:
                    for line in lines:
                        self.node.log(line)
                code = int(finish.group(1))
                text = '\n'.join(output)
                if check and code:
                    detail = '请查看上方该节点输出。' if display else self.node.redact(text)[-2000:]
                    raise RemoteError(f'远端操作返回状态 {code}: {detail}')
                return code, text
            # Keep an incomplete line, including partial end/sudo markers, until next read.
            lines = buffer.split('\n')
            buffer = lines.pop()
            for line in lines:
                line = line.rstrip('\r')
                output.append(line)
                if display:
                    self.node.log(line)
        raise RemoteError('SSH 会话中断或操作超时，远端结果未确认；重新获取凭据后检查 status/recover。')


class Node:
    def __init__(self, credentials):
        self.credentials = credentials
        self.client = self.shell = None
        self.stage = '/tmp/mom-remote-' + uuid.uuid4().hex
        self.stage_created = self.stage_mapped = False
        self.bundle = None
        self.deployment_started = False

    def redact(self, message):
        text = str(message)
        for secret in (self.credentials.password, self.credentials.sudo_password):
            if secret:
                text = text.replace(secret, '[已隐藏]')
        return re.sub(r'\x1b\[[0-?]*[ -/]*[@-~]', '', text)

    def log(self, message):
        text = self.redact(message)
        if text.strip():
            with CONSOLE:
                print(f'[{self.credentials.label}] {text}', flush=True)

    def connect(self):
        phase = 'SSH 登录'
        try:
            self.client = SystemSSH(self.credentials, self)
            self.client.connect()
            phase = '命令会话与目标 IP 核对'
            self.shell = Shell(self.client.channel, self)
            self.shell.run('stty -echo', display=False)
            _, text = self.python(IDENTITY, {'expected_ip': '10.32.15.' + self.credentials.label}, display=False)
            identity = self.data(text)
            self.log(f'已连接 {identity["hostname"]} / {identity["ip"]}')
            phase = 'SFTP 复用连接与小文件往返'
            probe_path = self.stage + '/probe.txt'
            content = ('MOM transfer probe ' + uuid.uuid4().hex + '\n').encode()
            with tempfile.TemporaryDirectory(prefix='mom-probe-') as folder:
                local_probe, local_copy = Path(folder) / 'probe.txt', Path(folder) / 'copy.txt'
                local_probe.write_bytes(content)
                self.stage_created = True
                self.client.sftp(['mkdir ' + sftp_quote(self.stage), 'chmod 700 ' + sftp_quote(self.stage),
                                  'put ' + sftp_quote(local_probe) + ' ' + sftp_quote(probe_path),
                                  'get ' + sftp_quote(probe_path) + ' ' + sftp_quote(local_copy)], timeout=120)
                if local_copy.read_bytes() != content:
                    raise RemoteError('SFTP 小文件往返内容不一致。')
            phase = '命令会话核对传输文件'
            self.python(PROBE, {'path': probe_path, 'sha256': hashlib.sha256(content).hexdigest()})
            self.stage_mapped = True
        except Exception as error:
            detail = self.redact(error)
            raise RemoteError(f'{phase}失败: {detail}') from None

    def python(self, code, data, root=False, **kwargs):
        return self.shell.run(python_command(code, data, root), **kwargs)

    @staticmethod
    def data(text):
        records = [line[len('MOM_DATA:'):] for line in text.splitlines() if line.startswith('MOM_DATA:')]
        if len(records) != 1:
            raise RemoteError('没有收到有效远端结果，停止后续操作。')
        return json.loads(records[0])

    def sudo(self):
        code, _ = self.shell.run('sudo -n true', check=False, display=False)
        if code:
            if not self.credentials.sudo_password:
                raise RemoteError('该节点不是免密 sudo，请重新运行并提供 sudo 密码。')
            prompt = '__MOM_SUDO_' + uuid.uuid4().hex + '__'
            self.shell.run('sudo -S -p ' + shlex.quote(prompt) + ' -v', sudo_prompt=prompt, display=False)

    def upload(self, archive, root, digest):
        self.sudo()
        path = self.stage + '/package.tar.gz'
        self.log(f'系统 SFTP 正在上传镜像包（{archive.stat().st_size / 1048576:.1f} MB），完成后校验……')
        self.client.sftp(['put ' + sftp_quote(archive.resolve()) + ' ' + sftp_quote(path + '.part'),
                          'rename ' + sftp_quote(path + '.part') + ' ' + sftp_quote(path)])
        self.log('镜像包上传完成。')
        members = [root, root + '/templates'] + [root + '/' + name for name in sorted(FILES)]
        self.python(EXTRACT, {'archive': path, 'sha256': digest, 'members': members}, timeout=1800)
        self.bundle = self.stage + '/' + root

    def action(self, action):
        self.sudo()
        if action == 'deploy':
            self.deployment_started = True
        self.python(ROOT_ACTION, {'script': self.bundle + '/deploy.sh', 'action': action}, root=True, timeout=1800)

    def check(self, challenge):
        self.action('check')
        _, result = self.python(SHARED, {'module': self.bundle + '/deploy.py', 'challenge': challenge},
                                root=True, display=False)
        return self.data(result)['fingerprint']

    def cleanup(self):
        if not self.stage_created:
            return
        try:
            if self.stage_mapped:
                self.python(CLEANUP, {'path': self.stage}, display=False, timeout=20)
            else:
                self.client.sftp(['-rm ' + sftp_quote(self.stage + '/probe.txt'),
                                  'rmdir ' + sftp_quote(self.stage)], timeout=20)
            self.log('本次 /tmp 上传目录已清理。')
        except Exception:
            self.log('本次上传目录未能清理: ' + self.stage)

    def close(self):
        if self.client:
            self.client.close()


def parallel(nodes, title, function):
    print(f'\n{title}', flush=True)
    results, failures = {}, {}
    with ThreadPoolExecutor(max_workers=2) as workers:
        pending = {workers.submit(function, node): node for node in nodes}
        for future in as_completed(pending):
            node = pending[future]
            try:
                results[node.credentials.label] = future.result()
                node.log('本阶段完成。')
            except Exception as error:
                message = str(error) if isinstance(error, RemoteError) else '连接或远端操作失败，结果未确认。'
                failures[node.credentials.label] = message
                node.log(message)
    return results, failures


def orchestrate(nodes, archive=None, info=None, action='deploy'):
    completed = False
    successful_nodes = set()
    try:
        _, failures = parallel(nodes, '并行连接两台，核对目标并测试小文件传输', lambda node: node.connect())
        if failures:
            raise RemoteError('连接/传输预检未全部通过，没有开始上传镜像或部署容器。')
        if action == 'probe':
            completed = True
            print('两台命令会话和 SFTP 复用测试均通过。')
            return
        root, digest = info
        _, failures = parallel(nodes, '并行上传、校验并解压离线包', lambda node: node.upload(archive, root, digest))
        if failures:
            raise RemoteError('镜像上传未全部完成，没有开始替换容器。')
        _, failures = parallel(nodes, '两台准备配置目录（不覆盖已有配置）', lambda node: node.action('init'))
        if failures:
            raise RemoteError('配置初始化未全部成功，没有开始替换容器。')
        if action == 'prepare':
            completed = True
            print('两台配置模板已准备；填写 /etc/mom/identity.env 和 deploy.json 后重新运行部署。')
            return
        challenge = os.urandom(32).hex()
        fingerprints, failures = parallel(nodes, '两台检查配置、网段、端口和离线包', lambda node: node.check(challenge))
        if failures:
            raise RemoteError('配置检查未全部通过，没有开始替换容器；首次使用先填写服务器配置。')
        if len(set(fingerprints.values())) != 1:
            raise RemoteError('两台数据库、Redis、JWT 或 Cookie 共享配置不一致，没有开始替换容器。')
        if action == 'check':
            completed = True
            print('两台检查通过，未部署容器；数据库迁移版本仍需由操作者确认。')
            return
        results, failures = parallel(nodes, '两台并行导入镜像并部署后端（允许中断）', lambda node: node.action('deploy'))
        successful_nodes.update(results)
        for node in nodes:
            label = node.credentials.label
            node.log('部署成功。' if label in results else '部署失败或结果未确认；检查该节点 status/recover。')
        if failures:
            raise RemoteError('本次并行部署未全部成功，成功节点保持运行；不自动回滚另一台或数据库。')
        completed = True
        print('177、178 后端均部署成功；前端和 176 Nginx 继续单独准备。')
    finally:
        for node in nodes:
            if completed or not node.deployment_started or node.credentials.label in successful_nodes:
                node.cleanup()
            else:
                node.log('保留部署目录供核查: ' + (node.bundle or node.stage))
            try:
                node.close()
            except Exception:
                node.log('连接关闭时出现错误，请核对该节点最终状态。')


def main():
    parser = argparse.ArgumentParser(description='本机输入两台临时 SSH，复用连接并行传输和部署；当前允许后端中断。')
    parser.add_argument('archive', nargs='?', type=Path, help='package.sh 生成的 mom-backend-版本.tar.gz')
    parser.add_argument('--action', choices=['probe', 'prepare', 'check', 'deploy'], default='deploy',
                        help='probe 仅小文件测试；prepare 首次建配置；check 两台预检；默认并行部署')
    args = parser.parse_args()
    for command in ('ssh', 'sftp'):
        if not shutil.which(command):
            raise RemoteError(f'缺少系统命令 {command}。')
    if args.action != 'probe':
        print('正在核对本地离线包……', flush=True)
        info = package_info(args.archive)
    else:
        info = None
    nodes = [Node(item) for item in collect_credentials()]
    orchestrate(nodes, args.archive, info, args.action)


if __name__ == '__main__':
    try:
        main()
    except (RemoteError, KeyboardInterrupt, EOFError, getpass.GetPassWarning) as error:
        print('远程部署未完成: ' + (str(error) or '已中断。'), file=sys.stderr)
        sys.exit(1)
