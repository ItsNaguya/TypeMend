"""Deploy the TypeMend web page to Server 2 (typemend.naguya.tech) with the SSH key.

  python deploy.py            back up the live site, upload changed files
  python deploy.py --dry-run  show what would be uploaded

The site is static: Apache serves REMOTE directly (rules in public/.htaccess).
Only that folder is touched; backups go to /var/www/vhosts/naguya.tech/typemend_backups.
"""
import hashlib, os, subprocess, sys, time
from pathlib import Path

# Server and key come from deploy.local next to this file (git-ignored), one 'name = value' per line: host, key.
_conf = {}
_local = Path(__file__).resolve().parent / 'deploy.local'
if _local.exists():
    for _line in _local.read_text(encoding='utf-8').splitlines():
        if '=' in _line:
            _k, _v = _line.split('=', 1)
            _conf[_k.strip()] = _v.strip()
KEY = os.environ.get('NAGU_SSH_KEY', _conf.get('key', ''))
HOST = os.environ.get('NAGU_SSH_HOST', _conf.get('host', ''))
if not KEY or not HOST:
    sys.exit('Set host and key in web/deploy.local (or NAGU_SSH_HOST / NAGU_SSH_KEY).')
REMOTE = '/var/www/vhosts/naguya.tech/typemend.naguya.tech'
BACKUPS = '/var/www/vhosts/naguya.tech/typemend_backups'
OWNER = 'xivantigravity:psaserv'
PUBLIC = Path(__file__).resolve().parent / 'public'


def ssh(cmd):
    r = subprocess.run(['plink', '-batch', '-i', KEY, HOST, cmd], capture_output=True, text=True)
    if r.returncode:
        sys.exit(f'remote command failed: {cmd}\n{r.stderr}')
    return r.stdout


def md5(p):
    return hashlib.md5(p.read_bytes()).hexdigest()


def main():
    dry = '--dry-run' in sys.argv
    local = {p.relative_to(PUBLIC).as_posix(): p for p in PUBLIC.rglob('*') if p.is_file()}
    remote = {}
    for line in ssh(f"cd {REMOTE} && find . -type f -exec md5sum {{}} +").splitlines():
        h, name = line.split(None, 1)
        remote[name[2:]] = h
    changed = [n for n, p in sorted(local.items()) if remote.get(n) != md5(p)]
    extra = sorted(set(remote) - set(local))
    print(f'{len(changed)} file(s) to upload:', *changed, sep='\n  ')
    if extra:
        print('On the server but not local (left alone):', *extra, sep='\n  ')
    if dry or not changed:
        return

    # the model and runtime are in git, so they are left out of the backup
    backup = f"{BACKUPS}/site_{time.strftime('%Y%m%d_%H%M%S')}.tar.gz"
    ssh(f"mkdir -p {BACKUPS} && tar --exclude=vendor --exclude=model -czf {backup} -C {REMOTE} .")
    print('backup:', backup)

    dirs = sorted({Path(n).parent.as_posix() for n in changed} - {'.'})
    if dirs:
        ssh('mkdir -p ' + ' '.join(f'{REMOTE}/{d}' for d in dirs))
    for n in changed:
        print('upload', n)
        subprocess.run(['pscp', '-batch', '-q', '-i', KEY, str(local[n]), f'{HOST}:{REMOTE}/{n}'], check=True)
    # only our own files: Plesk's .php-ini / .php-version and the folder itself keep their owner and mode
    ssh(f"cd {REMOTE} && find . -mindepth 1 ! -name '.php-*' -exec chown {OWNER} {{}} + && "
        f"find . -mindepth 1 -type d -exec chmod 755 {{}} + && find . -type f ! -name '.php-*' -exec chmod 644 {{}} +")
    print('done: https://typemend.naguya.tech/')


if __name__ == '__main__':
    main()
