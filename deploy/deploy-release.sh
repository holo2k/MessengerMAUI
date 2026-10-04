#!/usr/bin/env bash
set -Eeuo pipefail

incoming_root="/var/lib/messenger-deploy/incoming"
releases_root="/opt/messenger/releases"
current_link="/opt/messenger/current"
archive_input="${1:-}"
sha="${2:-}"

if [[ ! "${sha}" =~ ^[0-9a-f]{7,40}$ ]]; then
  echo "Invalid release SHA." >&2
  exit 2
fi
if [[ -z "${archive_input}" || ! -f "${archive_input}" ]]; then
  echo "Release archive does not exist." >&2
  exit 2
fi

archive="$(readlink -f -- "${archive_input}")"
case "${archive}" in
  "${incoming_root}"/*) ;;
  *) echo "Archive must be inside ${incoming_root}." >&2; exit 2 ;;
esac

release_dir="${releases_root}/${sha}"
staging_dir="${releases_root}/.${sha}.staging"
if [[ -e "${release_dir}" || -e "${staging_dir}" ]]; then
  echo "Release ${sha} already exists or is being prepared." >&2
  exit 2
fi

install -d -m 0755 -o root -g messenger "${staging_dir}"
cleanup_staging() { [[ ! -e "${staging_dir}" ]] || rm -rf -- "${staging_dir}"; }
trap cleanup_staging EXIT
tar -xzf "${archive}" -C "${staging_dir}" --no-same-owner --no-same-permissions

if [[ ! -x "${staging_dir}/api/Messenger.Api" || ! -x "${staging_dir}/efbundle" ]]; then
  echo "Release must contain executable api/Messenger.Api and efbundle." >&2
  exit 3
fi
chown -R root:messenger "${staging_dir}"
chmod -R o-rwx "${staging_dir}"
chmod -R g+rX "${staging_dir}"

set -a
# shellcheck disable=SC1091
source /etc/messenger/messenger.env
set +a
"${staging_dir}/efbundle" --connection "${ConnectionStrings__Messenger}"

mv -- "${staging_dir}" "${release_dir}"
trap - EXIT
previous_release="$(readlink -f -- "${current_link}" 2>/dev/null || true)"
ln -sfn "${release_dir}" "${current_link}.next"
mv -Tf -- "${current_link}.next" "${current_link}"

systemctl restart messenger-api.service
healthy=false
for _ in {1..30}; do
  if curl --fail --silent --show-error http://127.0.0.1:5192/health >/dev/null; then
    healthy=true
    break
  fi
  sleep 1
done

if [[ "${healthy}" != true ]]; then
  echo "New release failed its health check; restoring the previous release." >&2
  if [[ -n "${previous_release}" && -d "${previous_release}" ]]; then
    ln -sfn "${previous_release}" "${current_link}.rollback"
    mv -Tf -- "${current_link}.rollback" "${current_link}"
    systemctl restart messenger-api.service
  else
    systemctl stop messenger-api.service || true
  fi
  exit 4
fi

rm -f -- "${archive}"
mapfile -t stale_releases < <(find "${releases_root}" -mindepth 1 -maxdepth 1 -type d -printf '%T@ %p\n' | sort -rn | tail -n +6 | cut -d' ' -f2-)
for candidate in "${stale_releases[@]}"; do
  resolved="$(readlink -f -- "${candidate}")"
  case "${resolved}" in
    "${releases_root}"/*) [[ "${resolved}" == "$(readlink -f -- "${current_link}")" ]] || rm -rf -- "${resolved}" ;;
    *) echo "Refusing to remove unexpected path: ${resolved}" >&2 ;;
  esac
done

echo "Release ${sha} is active."
