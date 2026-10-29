"""No network: verify SSH framing, literal arguments and the two-node phase barrier."""
import ast
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import pty
import re
import select
import subprocess
import sys
import tarfile
import tempfile
import threading
import types
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('mom_remote', ROOT / 'remote.py')
remote = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = remote
spec.loader.exec_module(remote)


def credential(label='177'):
    return remote.Credentials(label, 'jump.example', 2222, 'JMS-' + label, 'temporary-secret', 'sudo-secret')


class ParserChecks(unittest.TestCase):
    def test_user_and_port_in_both_supported_orders(self):
        expected = ('JMS-token', 'jump.example', 2222)
        self.assertEqual(remote.parse_ssh('ssh JMS-token@jump.example -p 2222'), expected)
        self.assertEqual(remote.parse_ssh('ssh -p 2222 JMS-token@jump.example'), expected)

    def test_options_commands_and_appended_passwords_rejected(self):
        for text in ['ssh x@jump.example -oProxyCommand=evil', 'ssh x@jump.example id',
                     'ssh x@jump.example -p 2222,password', 'ssh x@jump.example -p 0',
                     'ssh x@jump.example;touch /tmp/pwned', 'ssh x@jump.example，password']:
            with self.subTest(text=text), self.assertRaises(remote.RemoteError):
                remote.parse_ssh(text)

    def test_passwords_not_in_credential_representation(self):
        self.assertNotIn('temporary-secret', repr(credential()))
        self.assertNotIn('sudo-secret', repr(credential()))

    def test_remote_arguments_are_literal_even_with_shell_metacharacters(self):
        with tempfile.TemporaryDirectory() as folder:
            marker = Path(folder) / 'must-not-exist'
            data = {'value': '$(touch ' + str(marker) + ');`touch ' + str(marker) + '`\n"quoted"'}
            command = remote.python_command('print(json.dumps(p))', data)
            result = subprocess.run(['bash', '-c', command], text=True, capture_output=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(json.loads(result.stdout), data)
            self.assertFalse(marker.exists())

    def test_embedded_python_programs_parse(self):
        for name in ('IDENTITY', 'PROBE', 'CLEANUP', 'EXTRACT', 'ROOT_ACTION', 'SHARED'):
            ast.parse(getattr(remote, name))


class PtyChannel:
    """A local interactive Bash for shell framing checks, never SSH."""
    def __init__(self):
        self.pid, self.fd = pty.fork()
        if not self.pid:
            os.execv('/bin/bash', ['/bin/bash', '--noprofile', '--norc'])
        self.closed = False

    def settimeout(self, _timeout):
        pass

    def sendall(self, value):
        os.write(self.fd, value.encode())

    def recv_ready(self):
        return bool(select.select([self.fd], [], [], 0)[0])

    def recv(self, count):
        return os.read(self.fd, count)

    def exit_status_ready(self):
        return self.closed

    def close(self):
        self.sendall('exit\n')
        os.waitpid(self.pid, 0)
        os.close(self.fd)
        self.closed = True


class ShellChecks(unittest.TestCase):
    def test_persistent_shell_handles_utf8_status_and_multiple_commands(self):
        channel = PtyChannel()
        self.addCleanup(channel.close)
        node = remote.Node(credential())
        shell = remote.Shell(channel, node)
        with contextlib.redirect_stdout(io.StringIO()):
            shell.run('stty -echo', display=False)
            code, text = shell.run("printf '中文输出\\n'; exit 7", check=False, display=False)
            self.assertEqual(code, 7)
            self.assertIn('中文输出', text)
            _, text = shell.run("printf 'still-connected\\n'", display=False)
            self.assertEqual(text.strip(), 'still-connected')
            _, text = shell.run(remote.python_command('print(json.dumps(p))', {'message': '多行 Python 命令'}),
                                display=False)
            self.assertEqual(json.loads(text), {'message': '多行 Python 命令'})

    def test_partial_markers_do_not_break_status_or_expose_password(self):
        class Channel:
            closed = False
            def settimeout(self, _value): pass
            def sendall(self, value):
                if 'MOM_BEGIN' not in value:
                    self.password_input = value
                    return
                begin = re.search('__MOM_BEGIN_[a-f0-9]+__', value).group()
                end = re.search('__MOM_END_[a-f0-9]+__', value).group()
                content = (value + '\r\n' + begin + '\r\nSUDO_PROMPT' + '\r\n' + end + ':0\r\n').encode()
                self.chunks = [content[index:index + 3] for index in range(0, len(content), 3)]
            def recv_ready(self): return bool(self.chunks)
            def recv(self, _count): return self.chunks.pop(0)
            def exit_status_ready(self): return False
        channel = Channel()
        node = remote.Node(credential())
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            remote.Shell(channel, node).run('sudo -S true', sudo_prompt='SUDO_PROMPT')
        self.assertEqual(channel.password_input, 'sudo-secret\n')
        self.assertNotIn('sudo-secret', output.getvalue())


class ArchiveChecks(unittest.TestCase):
    def archive(self, root, extra=None):
        release = 'test-01'
        bundle = root / ('mom-backend-' + release)
        bundle.mkdir()
        for name in remote.FILES:
            path = bundle / name
            path.parent.mkdir(exist_ok=True)
            path.write_text(json.dumps({'format': 1, 'release': release}) if name == 'manifest.json' else 'content')
        if extra:
            (bundle / extra).write_text('must-not-copy')
        archive = root / (bundle.name + '.tar.gz')
        with tarfile.open(archive, 'w:gz') as output:
            output.add(bundle, arcname=bundle.name)
        return archive

    def test_valid_package_has_stable_name_and_digest(self):
        with tempfile.TemporaryDirectory() as folder:
            archive = self.archive(Path(folder))
            name, digest = remote.package_info(archive)
            self.assertEqual(name, 'mom-backend-test-01')
            self.assertRegex(digest, r'^[0-9a-f]{64}$')

    def test_unexpected_private_file_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            archive = self.archive(Path(folder), 'secrets.json')
            with self.assertRaises(remote.RemoteError):
                remote.package_info(archive)


class ConnectionChecks(unittest.TestCase):
    def test_shell_and_sftp_use_one_authenticated_connection(self):
        class Client:
            def __init__(self):
                self.calls = 0; self.files = {}; self.closed = False; self.channel = object()
            def connect(self): self.calls += 1
            def sftp(self, commands, **_kwargs):
                for command in commands:
                    words = remote.shlex.split(command)
                    if words[0] == 'put':
                        self.files[words[2]] = Path(words[1]).read_bytes()
                    elif words[0] == 'get':
                        Path(words[2]).write_bytes(self.files[words[1]])
            def close(self): self.closed = True
        client = Client()
        node = remote.Node(credential())
        def python(code, data, **_kwargs):
            if code == remote.IDENTITY:
                return 0, 'MOM_DATA:' + json.dumps({'hostname': 'ZS02', 'ip': '10.32.15.177'})
            self.assertEqual(code, remote.PROBE)
            self.assertEqual(remote.hashlib.sha256(client.files.pop(data['path'])).hexdigest(), data['sha256'])
            return 0, 'probe verified'
        with patch.object(remote, 'SystemSSH', return_value=client), \
                patch.object(remote, 'Shell'), patch.object(node, 'python', python), \
                contextlib.redirect_stdout(io.StringIO()):
            node.connect()
            node.close()
        self.assertEqual(client.calls, 1)
        self.assertTrue(client.closed and node.stage_mapped)
        self.assertFalse(client.files)

    def test_system_sftp_reuses_socket_and_forbids_fresh_authentication(self):
        node = remote.Node(credential())
        client = remote.SystemSSH(node.credentials, node)
        self.addCleanup(client.close)
        client.channel = types.SimpleNamespace(exit_status_ready=lambda: False, close=lambda: None)
        client.socket.touch()
        with patch.object(remote.subprocess, 'run', return_value=types.SimpleNamespace(returncode=0, stdout='')) as run:
            client.sftp(['put "local file" "/tmp/remote file"'])
        command = run.call_args.args[0]
        self.assertEqual(command[0], 'sftp')
        self.assertIn('ControlPath=' + str(client.socket), command)
        self.assertIn('ProxyCommand=false', command)
        self.assertIn('BatchMode=yes', command)
        self.assertNotIn(node.credentials.password, str(run.call_args))
        self.assertIn('put "local file"', run.call_args.kwargs['input'])

    def test_missing_socket_stops_sftp_without_reauthenticating(self):
        node = remote.Node(credential())
        client = remote.SystemSSH(node.credentials, node)
        self.addCleanup(client.close)
        with patch.object(remote.subprocess, 'run') as run, self.assertRaisesRegex(remote.RemoteError, '连接已断开'):
            client.sftp(['pwd'])
        run.assert_not_called()

    def test_sftp_error_reports_native_diagnostic_and_hides_password(self):
        node = remote.Node(credential())
        client = remote.SystemSSH(node.credentials, node)
        self.addCleanup(client.close)
        client.channel = types.SimpleNamespace(exit_status_ready=lambda: False, close=lambda: None)
        client.socket.touch()
        result = types.SimpleNamespace(returncode=255, stdout='subsystem request failed temporary-secret')
        with patch.object(remote.subprocess, 'run', return_value=result), \
                self.assertRaisesRegex(remote.RemoteError, 'subsystem request failed') as raised:
            client.sftp(['pwd'])
        self.assertNotIn(node.credentials.password, str(raised.exception))

    def test_native_process_terminal_authentication_and_shell_without_network(self):
        node = remote.Node(credential())
        client = remote.SystemSSH(node.credentials, node)
        self.addCleanup(client.close)
        # A local program simulates the system ssh password prompt and control socket.
        program = ('import os,sys; from pathlib import Path; '
                   'value=input("Password: "); '
                   'sys.exit(7) if value != "temporary-secret" else None; '
                   'Path(sys.argv[1]).touch(); '
                   'os.execv("/bin/bash",["bash","--noprofile","--norc"])')
        client.channel = remote.ProcessChannel([sys.executable, '-c', program, str(client.socket)])
        client.authenticate()
        with contextlib.redirect_stdout(io.StringIO()):
            shell = remote.Shell(client.channel, node)
            shell.run('stty -echo', display=False)
            code, text = shell.run('printf "原生终端正常\\n"', display=False)
        self.assertEqual(code, 0)
        self.assertEqual(text.strip(), '原生终端正常')

    def test_failure_identifies_connection_stage(self):
        node = remote.Node(credential())
        with patch.object(remote, 'SystemSSH') as client:
            client.return_value.connect.side_effect = remote.RemoteError('Connection refused')
            with self.assertRaisesRegex(remote.RemoteError, 'SSH 登录失败: Connection refused'):
                node.connect()

    def test_sftp_paths_reject_command_injection_and_quote_spaces(self):
        self.assertEqual(remote.sftp_quote('/tmp/a b"c'), '"/tmp/a b\\"c"')
        with self.assertRaises(remote.RemoteError):
            remote.sftp_quote('/tmp/a\n!touch /tmp/should-not-exist')


class FakeNode:
    def __init__(self, label, events, barrier, failing=None, fingerprint='shared'):
        self.credentials = credential(label)
        self.events, self.barrier = events, barrier
        self.failing, self.fingerprint = failing, fingerprint
        self.deployment_started = False
        self.bundle, self.stage = '/tmp/test/bundle', '/tmp/test'
        self.cleaned = self.closed = False

    def record(self, phase):
        self.events.append((self.credentials.label, phase))
        if phase in ('connect', 'deploy'):
            self.barrier.wait(timeout=2)
        if phase == self.failing:
            raise remote.RemoteError('simulated failure')

    def log(self, _text): pass
    def connect(self): self.record('connect')
    def upload(self, *_args): self.record('upload')
    def action(self, action):
        if action == 'deploy':
            self.deployment_started = True
        self.record(action)
    def check(self, _challenge):
        self.record('check')
        return self.fingerprint
    def cleanup(self): self.cleaned = True
    def close(self): self.closed = True


class ParallelChecks(unittest.TestCase):
    def nodes(self, failing=None, mismatch=False):
        events, barrier = [], threading.Barrier(2)
        nodes = [FakeNode('177', events, barrier),
                 FakeNode('178', events, barrier, failing, 'different' if mismatch else 'shared')]
        return nodes, events

    def execute(self, nodes, action='deploy'):
        with contextlib.redirect_stdout(io.StringIO()):
            remote.orchestrate(nodes, Path('/tmp/not-a-real-archive'), ('bundle', 'digest'), action)

    def test_both_nodes_reach_deploy_in_parallel_after_all_checks(self):
        nodes, events = self.nodes()
        self.execute(nodes)
        deploy_index = min(i for i, (_, phase) in enumerate(events) if phase == 'deploy')
        self.assertTrue(all((label, 'check') in events[:deploy_index] for label in ('177', '178')))
        self.assertTrue(all(node.cleaned and node.closed for node in nodes))

    def test_any_connection_failure_prevents_all_uploads(self):
        nodes, events = self.nodes('connect')
        with self.assertRaises(remote.RemoteError): self.execute(nodes)
        self.assertFalse(any(phase == 'upload' for _, phase in events))
        self.assertTrue(all(node.closed for node in nodes))

    def test_any_preflight_failure_prevents_both_deployments(self):
        nodes, events = self.nodes('check')
        with self.assertRaises(remote.RemoteError): self.execute(nodes)
        self.assertFalse(any(phase == 'deploy' for _, phase in events))

    def test_shared_configuration_mismatch_prevents_deployment(self):
        nodes, events = self.nodes(mismatch=True)
        with self.assertRaisesRegex(remote.RemoteError, '共享配置不一致'): self.execute(nodes)
        self.assertFalse(any(phase == 'deploy' for _, phase in events))

    def test_one_node_failure_is_reported_without_rolling_back_other(self):
        nodes, events = self.nodes('deploy')
        with self.assertRaisesRegex(remote.RemoteError, '未全部成功'): self.execute(nodes)
        self.assertTrue(all((label, 'deploy') in events for label in ('177', '178')))
        self.assertFalse(any(phase == 'rollback' for _, phase in events))
        self.assertTrue(all(node.closed for node in nodes))
        self.assertTrue(nodes[0].cleaned)
        self.assertFalse(nodes[1].cleaned)

    def test_probe_only_opens_connections_and_cleans_up(self):
        nodes, events = self.nodes()
        self.execute(nodes, 'probe')
        self.assertEqual(set(phase for _, phase in events), {'connect'})
        self.assertTrue(all(node.cleaned for node in nodes))

    def test_prepare_never_checks_or_deploys_containers(self):
        nodes, events = self.nodes()
        self.execute(nodes, 'prepare')
        self.assertFalse(any(phase in ('check', 'deploy') for _, phase in events))


if __name__ == '__main__':
    unittest.main()
