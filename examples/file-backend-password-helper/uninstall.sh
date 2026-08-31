#!/bin/bash
# Origin: GPT-5.3 Codex
# Context: Optional Authelia file-backend password helper
# Target: Authelia host/container host running systemd
# Purpose: Disable and remove helper service files

set -euo pipefail

INSTALL_DIR="${INSTALL_DIR:-/opt/authelia-password-helper}"
ENV_FILE="${ENV_FILE:-/etc/authelia-password-helper.env}"
SERVICE_FILE="${SERVICE_FILE:-/etc/systemd/system/authelia-password-helper.service}"
KEEP_ENV="${KEEP_ENV:-false}"

section() { echo; echo "===== $* ====="; }
pass() { echo "PASS: $*"; }

[ "$(id -u)" = "0" ] || { echo "FAIL: Run as root"; exit 1; }

section "Stop service"
systemctl disable --now authelia-password-helper.service 2>/dev/null || true

section "Remove files"
rm -f "$SERVICE_FILE"
rm -rf "$INSTALL_DIR"
if [ "$KEEP_ENV" = "true" ]; then
  echo "Keeping env file: $ENV_FILE"
else
  rm -f "$ENV_FILE"
fi

section "Reload systemd"
systemctl daemon-reload
systemctl reset-failed authelia-password-helper.service 2>/dev/null || true

section "Done"
pass "Removed authelia-password-helper"
