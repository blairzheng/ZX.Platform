"""Exercise deployment and failure recovery with a stateful fake Docker daemon."""
import contextlib
import copy
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import tempfile
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('mom_deploy', ROOT / 'deploy.py')
deploy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(deploy)
OLD_IMAGES = {service: {'id': 'sha256:' + char * 64} for service, char in [('identity', '1'), ('gateway', '2')]}
NEW_IMAGES = {service: {'id': 'sha256:' + char * 64} for service, char in [('identity', '3'), ('gateway', '4')]}


class FakeDocker:
    def __init__(self):
        self.containers = {}
        self.calls = []
        self.sequence = 0
        self.fail_gateway = False

    def lookup(self, ref):
        for info in self.containers.values():
            if ref in (info['Id'], info['Name'].lstrip('/')):
                return info
        return None

    def inspect(self, ref):
        return copy.deepcopy(self.lookup(ref))

    def add(self, name, image, address, transaction='old'):
        self.sequence += 1
        info = {'Id': str(self.sequence), 'Name': '/' + name, 'Image': image,
                'State': {'Running': True, 'Status': 'running'}, 'RestartCount': 0,
                'Config': {'Image': 'mom-' + name.split('-')[1] + ':old',
                           'Labels': {deploy.LABEL: '1', deploy.LABEL + '.transaction': transaction}},
                'NetworkSettings': {'Networks': {'mom-net': {'IPAddress': address, 'Aliases': [name]}}}}
        if any(address == endpoint['IPAddress'] for item in self.containers.values()
               for endpoint in item['NetworkSettings']['Networks'].values()):
            raise deploy.DeploymentError('IP already allocated')
        self.containers[info['Id']] = info
        return info

    def __call__(self, *args):
        self.calls.append(args)
        if args[0] == 'run':
            name = args[args.index('--name') + 1]
            transaction = next(value.split('=', 1)[1] for value in args if value.startswith(deploy.LABEL + '.transaction='))
            info = self.add(name, args[-1], args[args.index('--ip') + 1], transaction)
            if name == 'mom-gateway' and self.fail_gateway:
                self.fail_gateway = False
                info['State']['Running'] = False
                raise deploy.DeploymentError('simulated gateway bind failure')
            return info['Id']
        if args[0] in ('stop', 'start', 'rename', 'rm'):
            ref = args[-2] if args[0] == 'rename' else args[-1]
            info = self.lookup(ref)
            if not info:
                raise deploy.DeploymentError('missing container')
            if args[0] == 'stop':
                info['State'] = {'Running': False, 'Status': 'exited'}
            elif args[0] == 'start':
                info['State'] = {'Running': True, 'Status': 'running'}
            elif args[0] == 'rename':
                if self.lookup(args[-1]):
                    raise deploy.DeploymentError('name already allocated')
                info['Name'] = '/' + args[-1]
            else:
                self.containers.pop(info['Id'])
            return ''
        if args[:2] == ('network', 'disconnect'):
            self.lookup(args[-1])['NetworkSettings']['Networks'].pop(args[-2])
            return ''
        if args[:2] == ('network', 'connect'):
            info = self.lookup(args[-1])
            address = args[args.index('--ip') + 1]
            if any(address == endpoint['IPAddress'] for item in self.containers.values()
                   for endpoint in item['NetworkSettings']['Networks'].values()):
                raise deploy.DeploymentError('IP already allocated')
            info['NetworkSettings']['Networks'][args[-2]] = {'IPAddress': address, 'Aliases': []}
            return ''
        if args[:2] == ('image', 'inspect'):
            return json.dumps([{'Id': args[-1], 'Os': 'linux', 'Architecture': 'amd64'}])
        return ''


class DeploymentChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.config = Path(self.temp.name) / 'config'
        self.app = deploy.Deployment(ROOT, self.config)
        self.output = contextlib.redirect_stdout(io.StringIO())
        self.output.__enter__()
        self.addCleanup(self.output.__exit__, None, None, None)
        self.app.init()
        self.settings = dict(deploy.DEFAULTS)
        self.fake = FakeDocker()
        self.ids = {}
        for service in deploy.NAMES:
            info = self.fake.add(deploy.NAMES[service], OLD_IMAGES[service]['id'], self.settings[service + '_ip'])
            self.ids[service] = info['Id']
        deploy.atomic_json(self.app.state_file, {'release': 'old', 'images': OLD_IMAGES,
                                               'settings': self.settings, 'transaction': 'old'})
        for name, replacement in [('docker', self.fake), ('inspect', self.fake.inspect), ('wait_ready', lambda *_args: None)]:
            started = patch.object(deploy, name, replacement)
            started.start()
            self.addCleanup(started.stop)

    def test_init_never_overwrites_private_config(self):
        path = self.config / 'identity.env'
        path.write_text('PRIVATE_CONFIG=keep-me\n')
        self.app.init()
        self.assertEqual(path.read_text(), 'PRIVATE_CONFIG=keep-me\n')
        self.assertEqual(path.stat().st_mode & 0o777, 0o600)

    def test_placeholder_validation_happens_before_container_stop(self):
        with self.assertRaises(deploy.DeploymentError):
            self.app.preflight()
        self.assertFalse(self.fake.calls)

    def test_env_values_are_not_executed_or_exposed(self):
        path = self.config / 'identity.env'
        marker = self.config / 'must-not-exist'
        secret = '$(touch ' + str(marker) + ');Pass<sensitive'
        path.write_text('DATABASE=' + secret + '\n')
        self.assertEqual(deploy.read_env(path)['DATABASE'], secret)
        self.assertFalse(marker.exists())
        path.write_text('PASSWORD=' + secret + '\nPASSWORD=duplicate\n')
        with self.assertRaises(deploy.DeploymentError) as caught:
            deploy.read_env(path)
        self.assertNotIn(secret, str(caught.exception))

    def test_success_preserves_previous_containers_and_pins_images(self):
        self.app.replace(NEW_IMAGES, 'new', self.settings)
        state = self.app.state()
        self.assertEqual(state['release'], 'new')
        self.assertEqual(len(state['previous']), 2)
        self.assertFalse(self.app.pending_file.exists())
        for service, name in deploy.NAMES.items():
            self.assertEqual(self.fake.inspect(name)['Image'], NEW_IMAGES[service]['id'])
            self.assertFalse(self.fake.inspect(self.ids[service])['State']['Running'])
            self.assertNotIn('mom-net', self.fake.inspect(self.ids[service])['NetworkSettings']['Networks'])

    def test_partial_creation_failure_restores_both_originals(self):
        self.fake.fail_gateway = True
        with self.assertRaises(deploy.DeploymentError):
            self.app.replace(NEW_IMAGES, 'broken', self.settings)
        self.assertEqual(self.app.state()['release'], 'old')
        self.assertFalse(self.app.pending_file.exists())
        self.assertEqual(len(self.fake.containers), 2)
        for service, name in deploy.NAMES.items():
            info = self.fake.inspect(name)
            self.assertEqual(info['Id'], self.ids[service])
            self.assertTrue(info['State']['Running'])
            self.assertEqual(info['NetworkSettings']['Networks']['mom-net']['IPAddress'], self.settings[service + '_ip'])

    def test_readiness_failure_restores_old_containers(self):
        with patch.object(deploy, 'wait_ready', side_effect=deploy.DeploymentError('not ready')):
            with self.assertRaises(deploy.DeploymentError):
                self.app.replace(NEW_IMAGES, 'broken', self.settings)
        self.assertEqual(self.fake.inspect('mom-identity')['Id'], self.ids['identity'])
        self.assertEqual(self.app.state()['release'], 'old')

    def test_first_deploy_failure_cleans_only_new_containers(self):
        self.fake.containers.clear()
        self.app.state_file.unlink()
        self.fake.fail_gateway = True
        with self.assertRaises(deploy.DeploymentError):
            self.app.replace(NEW_IMAGES, 'broken', self.settings)
        self.assertFalse(self.fake.containers)
        self.assertFalse(self.app.state_file.exists())
        self.assertFalse(self.app.pending_file.exists())

    def test_failed_deploy_keeps_previously_stopped_identity_stopped(self):
        self.fake.lookup('mom-identity')['State'] = {'Running': False, 'Status': 'exited'}
        self.fake.fail_gateway = True
        with self.assertRaises(deploy.DeploymentError):
            self.app.replace(NEW_IMAGES, 'broken', self.settings)
        self.assertFalse(self.fake.inspect('mom-identity')['State']['Running'])

    def test_manual_docker_port_binding_is_recognized(self):
        self.fake.lookup('mom-gateway')['HostConfig'] = {
            'PortBindings': {'8080/tcp': [{'HostIp': '', 'HostPort': '18080'}]}}
        with patch.object(deploy.socket, 'socket', side_effect=AssertionError('should reuse own binding')):
            deploy.port_check(self.settings)

    def test_rollback_uses_old_containers_without_reading_modified_env(self):
        self.app.replace(NEW_IMAGES, 'new', self.settings)
        (self.config / 'identity.env').write_text('INVALID=changed\n')
        self.app.rollback()
        self.assertEqual(self.app.state()['release'], 'old')
        self.assertEqual(self.fake.inspect('mom-identity')['Id'], self.ids['identity'])
        self.assertEqual(self.fake.inspect('mom-gateway')['Id'], self.ids['gateway'])
        self.assertEqual(len(self.fake.containers), 4)

    def test_failed_rollback_restores_new_version(self):
        self.app.replace(NEW_IMAGES, 'new', self.settings)
        new_id = self.fake.inspect('mom-identity')['Id']
        with patch.object(deploy, 'wait_ready', side_effect=deploy.DeploymentError('not ready')):
            with self.assertRaises(deploy.DeploymentError):
                self.app.rollback()
        self.assertEqual(self.app.state()['release'], 'new')
        self.assertEqual(self.fake.inspect('mom-identity')['Id'], new_id)
        self.assertFalse(self.app.pending_file.exists())

    def test_recovery_after_kill_between_stop_and_rename(self):
        info = self.fake.inspect('mom-identity')
        deploy.atomic_json(self.app.pending_file, {
            'id': 'interrupted', 'kind': 'deploy', 'targets': [],
            'originals': [{'id': info['Id'], 'name': 'mom-identity', 'running': True,
                           'backup': 'mom-identity.previous-interrupted',
                           'endpoint': info['NetworkSettings']['Networks']['mom-net']}],
        })
        self.fake('stop', '--time', '30', info['Id'])
        self.app.recover()
        self.assertTrue(self.fake.inspect('mom-identity')['State']['Running'])
        self.assertFalse(self.app.pending_file.exists())

    def test_only_one_previous_generation_is_retained(self):
        self.app.replace(NEW_IMAGES, 'new', self.settings)
        self.app.replace(NEW_IMAGES, 'newer', self.settings)
        self.assertEqual(len(self.fake.containers), 4)
        self.assertTrue(all(self.fake.inspect(ref) is None for ref in self.ids.values()))


class BundleChecks(unittest.TestCase):
    def test_tampered_archive_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'images.tar').write_bytes(b'original')
            digest = hashlib.sha256(b'original').hexdigest()
            (root / 'SHA256SUMS').write_text(digest + '  images.tar\n')
            (root / 'images.tar').write_bytes(b'tampered')
            with self.assertRaisesRegex(deploy.DeploymentError, '校验失败'):
                deploy.bundle_manifest(root)

    def test_parent_traversal_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'SHA256SUMS').write_text('0' * 64 + '  ../secrets.json\n')
            with self.assertRaisesRegex(deploy.DeploymentError, '无效路径'):
                deploy.bundle_manifest(root)


class CookieConfigurationChecks(unittest.TestCase):
    def validate(self, cookie):
        with tempfile.TemporaryDirectory() as folder:
            config = Path(folder)
            (config / 'gateway.env').write_text('# no overrides\n')
            (config / 'identity.env').write_text(
                'IdentityDatabase__Schema=SAJET\n'
                'ConnectionStrings__IdentityDatabase=User Id=app;Password=test;Data Source=test;\n'
                'ConnectionStrings__IdentityRedis=redis:26379,serviceName=mom-redis\n'
                'AuthenticationRedis__KeyPrefix=mom:test:\n'
                'AuthenticationJwt__SigningKey=MTExMTExMTExMTExMTExMTExMTExMTExMTExMTExMTE=\n'
                'AuthenticationJwt__Issuer=MOM.Test\n'
                'AuthenticationJwt__Audience=MOM.Test\n' + cookie)
            deploy.validate_env(config, deploy.DEFAULTS)

    def test_default_cookie_configuration_keeps_https(self):
        self.validate('')

    def test_http_requires_explicit_opt_in(self):
        with self.assertRaisesRegex(deploy.DeploymentError, 'HTTP 测试需同时设置'):
            self.validate('AuthenticationCookie__Secure=false\n')

    def test_http_configuration_accepted_without_forced_secure_override(self):
        self.validate('AuthenticationCookie__Secure=false\nAuthenticationCookie__AllowInsecureHttp=true\n')
        self.assertNotIn('AuthenticationCookie__Secure', deploy.transport('identity', deploy.DEFAULTS))

    def test_cookie_flag_typos_are_rejected(self):
        with self.assertRaisesRegex(deploy.DeploymentError, '必须是 true 或 false'):
            self.validate('AuthenticationCookie__Secure=flase\nAuthenticationCookie__AllowInsecureHttp=true\n')


class NetworkChecks(unittest.TestCase):
    def test_conflicting_server_route_is_rejected(self):
        with patch.object(deploy, 'docker', return_value=''), \
                patch.object(deploy, 'run', return_value='[{"dst":"172.30.0.0/16","dev":"eth0"}]'):
            with self.assertRaisesRegex(deploy.DeploymentError, '路由重叠'):
                deploy.network_check(deploy.DEFAULTS)

    def test_existing_mom_network_route_is_allowed(self):
        network = {'Name': 'mom-net', 'Id': 'abcdefghijklmno', 'Driver': 'bridge',
                   'IPAM': {'Config': [{'Subnet': deploy.DEFAULTS['subnet']}]}, 'Containers': {}}
        def docker(*args):
            return 'network-id' if args[1] == 'ls' else json.dumps([network])
        with patch.object(deploy, 'docker', docker), patch.object(deploy, 'run', return_value=json.dumps([
                {'dst': deploy.DEFAULTS['subnet'], 'dev': 'br-abcdefghijkl'}])):
            self.assertEqual(deploy.network_check(deploy.DEFAULTS), network)


class PackageChecks(unittest.TestCase):
    def test_real_package_script_with_fake_docker_creates_complete_bundle(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            scripts = root / 'deploy' / 'docker'
            scripts.mkdir(parents=True)
            for name in ('package.sh', 'deploy.sh', 'deploy.py', 'README.md'):
                shutil.copy(ROOT / name, scripts / name)
            shutil.copytree(ROOT / 'templates', scripts / 'templates')
            # Files outside the explicit allowlist must not enter the bundle.
            (root / 'secrets.json').write_text('must-not-be-packaged')
            (scripts / 'identity.env').write_text('must-not-be-packaged')
            tools_dir = root / 'tools'
            tools_dir.mkdir()
            fake = tools_dir / 'docker'
            fake.write_text('#!' + sys.executable + '\n' + '''
import json, os, sys
from pathlib import Path
state = Path(os.environ['MOM_PACKAGE_TEST_STATE'])
images = json.loads(state.read_text()) if state.exists() else {}
args = sys.argv[1:]
if args[:2] == ['image', 'inspect']:
    if args[2] not in images:
        sys.exit(1)
    print(json.dumps([images[args[2]]]))
elif args[:2] == ['buildx', 'build']:
    tag = args[args.index('--tag') + 1]
    images[tag] = {'Id': 'sha256:' + ('1' if 'identity' in tag else '2') * 64,
                   'Os': 'linux', 'Architecture': 'amd64'}
    state.write_text(json.dumps(images))
elif args[0] == 'save':
    Path(args[args.index('--output') + 1]).write_bytes(b'FAKE_OFFLINE_IMAGE_TAR')
''')
            fake.chmod(0o755)
            env = dict(os.environ, PATH=str(tools_dir) + os.pathsep + os.environ['PATH'],
                       MOM_PACKAGE_TEST_STATE=str(root / 'docker-state.json'))
            result = subprocess.run(['bash', str(scripts / 'package.sh'), 'test-01'],
                                    env=env, text=True, capture_output=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            archive = root / 'output/mom-deploy/mom-backend-test-01.tar.gz'
            spec = importlib.util.spec_from_file_location('mom_remote_package_check', ROOT / 'remote.py')
            remote = importlib.util.module_from_spec(spec)
            with patch.dict(sys.modules, {spec.name: remote}):
                spec.loader.exec_module(remote)
            bundle_name, digest = remote.package_info(archive)
            self.assertEqual(bundle_name, 'mom-backend-test-01')
            with tarfile.open(archive) as source:
                names = source.getnames()
                self.assertFalse(any('secrets.json' in name or name.endswith('/identity.env') for name in names))
                self.assertFalse(any(Path(name).name.startswith('._') for name in names))
            # Exercise the same strict extraction program used on the Linux servers.
            extraction_dir = root / 'received'
            extraction_dir.mkdir()
            received_archive = extraction_dir / archive.name
            shutil.copyfile(archive, received_archive)
            result = subprocess.run(['bash', '-c', remote.python_command(remote.EXTRACT, {
                'archive': str(received_archive), 'sha256': digest,
                'members': [bundle_name, bundle_name + '/templates'] +
                           [bundle_name + '/' + name for name in remote.FILES],
            })], text=True, capture_output=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            manifest = deploy.bundle_manifest(extraction_dir / bundle_name)
            self.assertEqual(manifest['release'], 'test-01')
            result = subprocess.run(['bash', str(scripts / 'package.sh'), 'test-01'],
                                    env=env, text=True, capture_output=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn('离线包已存在', result.stderr)


if __name__ == '__main__':
    unittest.main()
