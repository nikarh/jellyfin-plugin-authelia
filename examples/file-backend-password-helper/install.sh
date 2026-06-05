#!/bin/bash
# Origin: GPT-5.3 Codex
# Context: Optional Authelia file-backend password helper
# Target: Authelia host/container host running systemd and Docker
# Purpose: Install helper as systemd service with bearer token and optional IP allowlist

set -euo pipefail

INSTALL_DIR="${INSTALL_DIR:-/opt/authelia-password-helper}"
ENV_FILE="${ENV_FILE:-/etc/authelia-password-helper.env}"
SERVICE_FILE="${SERVICE_FILE:-/etc/systemd/system/authelia-password-helper.service}"
HELPER_SOURCE="$(cd "$(dirname "$0")" && pwd)/authelia_file_backend_password_helper.py"

BIND_HOST="${BIND_HOST:-127.0.0.1}"
PORT="${PORT:-9944}"
AUTHELIA_BASE="${AUTHELIA_BASE:-https://auth.example.com}"
TARGET_URL="${TARGET_URL:-https://jellyfin.example.com/}"
USERS_DB="${USERS_DB:-/opt/authelia/config/users_database.yml}"
AUTHELIA_CONTAINER="${AUTHELIA_CONTAINER:-authelia}"
ALLOWED_CLIENTS="${ALLOWED_CLIENTS:-}"
MIN_PASSWORD_LENGTH="${MIN_PASSWORD_LENGTH:-8}"

section() { echo; echo "===== $* ====="; }
fail() { echo "FAIL: $*"; exit 1; }
pass() { echo "PASS: $*"; }

[ "$(id -u)" = "0" ] || fail "Run as root"
[ -f "$HELPER_SOURCE" ] || fail "Helper source not found: $HELPER_SOURCE"
command -v systemctl >/dev/null || fail "systemctl not found"
command -v docker >/dev/null || fail "docker not found"

if [ -z "${HELPER_TOKEN:-}" ]; then
  if command -v openssl >/dev/null; then
    HELPER_TOKEN="$(openssl rand -hex 32)"
  else
    HELPER_TOKEN="$(head -c 32 /dev/urandom | od -An -tx1 | tr -d ' \n')"
  fi
  GENERATED_TOKEN="yes"
else
  GENERATED_TOKEN="no"
fi

section "Install files"
install -d -m 0750 "$INSTALL_DIR"
install -m 0750 "$HELPER_SOURCE" "$INSTALL_DIR/authelia_file_backend_password_helper.py"

section "Write environment"
cat > "$ENV_FILE" <<EOF
BIND_HOST=${BIND_HOST}
PORT=${PORT}
HELPER_TOKEN=${HELPER_TOKEN}
ALLOWED_CLIENTS=${ALLOWED_CLIENTS}
AUTHELIA_BASE=${AUTHELIA_BASE}
TARGET_URL=${TARGET_URL}
USERS_DB=${USERS_DB}
AUTHELIA_CONTAINER=${AUTHELIA_CONTAINER}
MIN_PASSWORD_LENGTH=${MIN_PASSWORD_LENGTH}
EOF
chmod 0600 "$ENV_FILE"

section "Write systemd unit"
cat > "$SERVICE_FILE" <<EOF
[Unit]
Description=Authelia File Backend Password Change Helper
After=network-online.target docker.service
Wants=network-online.target
Requires=docker.service

[Service]
Type=simple
User=root
Group=root
EnvironmentFile=${ENV_FILE}
ExecStart=/usr/bin/env python3 ${INSTALL_DIR}/authelia_file_backend_password_helper.py
Restart=on-failure
RestartSec=5
NoNewPrivileges=true
PrivateTmp=true
RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX

[Install]
WantedBy=multi-user.target
EOF
chmod 0644 "$SERVICE_FILE"

section "Enable service"
systemctl daemon-reload
systemctl enable --now authelia-password-helper.service
sleep 2
systemctl --no-pager --full status authelia-password-helper.service || true

section "Health check"
if curl -sk --max-time 10 "http://${BIND_HOST}:${PORT}/health" | grep -q '"OK"'; then
  pass "Helper health endpoint is reachable locally"
else
  fail "Helper health endpoint failed"
fi

section "Done"
pass "Installed authelia-password-helper"
echo "HELPER_URL=http://${BIND_HOST}:${PORT}"
if [ "$GENERATED_TOKEN" = "yes" ]; then
  echo "GENERATED_HELPER_TOKEN=${HELPER_TOKEN}"
  echo "Store this token in Jellyfin plugin config: Password change helper bearer token"
else
  echo "HELPER_TOKEN_PROVIDED=yes"
fi
if [ -n "$ALLOWED_CLIENTS" ]; then
  echo "ALLOWED_CLIENTS=${ALLOWED_CLIENTS}"
else
  echo "ALLOWED_CLIENTS=<disabled>"
fi
