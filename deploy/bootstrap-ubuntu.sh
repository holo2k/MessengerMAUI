#!/usr/bin/env bash
set -Eeuo pipefail

if [[ ${EUID} -ne 0 ]]; then
  echo "Run bootstrap-ubuntu.sh as root." >&2
  exit 1
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
install -d -m 0755 /etc/messenger /opt/messenger/releases /opt/messenger/infrastructure
install -d -m 0750 -o root -g root /var/lib/messenger-deploy/incoming

export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y ca-certificates curl docker.io docker-compose-v2 caddy ufw

getent group messenger >/dev/null || groupadd --system messenger
id messenger-api >/dev/null 2>&1 || useradd --system --gid messenger --home-dir /nonexistent --shell /usr/sbin/nologin messenger-api
id messenger-deploy >/dev/null 2>&1 || useradd --create-home --shell /bin/bash messenger-deploy
usermod --append --groups messenger messenger-deploy
chown root:messenger /etc/messenger
chmod 0750 /etc/messenger
chown root:messenger /opt/messenger /opt/messenger/releases
chmod 0755 /opt/messenger /opt/messenger/releases
chown messenger-deploy:messenger-deploy /var/lib/messenger-deploy/incoming
chmod 0750 /var/lib/messenger-deploy/incoming

install -m 0644 "${script_dir}/docker-compose.infrastructure.yml" /opt/messenger/infrastructure/compose.yml
install -m 0644 "${script_dir}/Dockerfile.minio" /opt/messenger/infrastructure/Dockerfile.minio
install -m 0644 "${script_dir}/Caddyfile" /etc/caddy/Caddyfile
install -m 0644 "${script_dir}/messenger-api.service" /etc/systemd/system/messenger-api.service
install -m 0755 "${script_dir}/deploy-release.sh" /usr/local/sbin/messenger-deploy
install -m 0440 "${script_dir}/messenger-deploy.sudoers" /etc/sudoers.d/messenger-deploy
visudo -cf /etc/sudoers.d/messenger-deploy

if [[ ! -f /etc/messenger/messenger.env ]]; then
  echo "Create /etc/messenger/messenger.env before bootstrap." >&2
  exit 1
fi
if [[ ! -f /opt/messenger/infrastructure/.env ]]; then
  echo "Create /opt/messenger/infrastructure/.env before bootstrap." >&2
  exit 1
fi
chown root:messenger /etc/messenger/messenger.env
chmod 0640 /etc/messenger/messenger.env
chown root:root /opt/messenger/infrastructure/.env
chmod 0600 /opt/messenger/infrastructure/.env

systemctl enable --now docker
minio_image="messenger-minio:RELEASE.2025-10-15T17-29-55Z"
if ! docker image inspect "${minio_image}" >/dev/null 2>&1; then
  if [[ -f /opt/messenger/infrastructure/minio-image.tar.gz ]]; then
    docker load --input /opt/messenger/infrastructure/minio-image.tar.gz
  else
    docker build --tag "${minio_image}" --file /opt/messenger/infrastructure/Dockerfile.minio /opt/messenger/infrastructure
  fi
fi
docker compose --env-file /opt/messenger/infrastructure/.env -f /opt/messenger/infrastructure/compose.yml up -d
systemctl daemon-reload
systemctl enable messenger-api.service
systemctl enable --now caddy
systemctl restart caddy

ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw --force enable

echo "Messenger server bootstrap completed."
