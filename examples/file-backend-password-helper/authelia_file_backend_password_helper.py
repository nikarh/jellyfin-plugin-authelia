#!/usr/bin/env python3
"""
Authelia file-backend password change helper.

This is a pragmatic homelab workaround for Authelia file-backend setups where
users have no deliverable email address for Elevated Session One-Time Codes.

Security model:
  - Jellyfin sends username/current password/new password to this helper.
  - Helper verifies the current password via Authelia /api/firstfactor.
  - Helper updates only the user's password hash in users_database.yml.
  - Helper restarts Authelia so the new file-backend hash is loaded.

This is NOT a general upstream Authelia replacement. Keep it private.
"""

from __future__ import annotations

import ipaddress
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


BIND_HOST = os.environ.get("BIND_HOST", "127.0.0.1")
PORT = int(os.environ.get("PORT", "9944"))
HELPER_TOKEN = os.environ.get("HELPER_TOKEN", "")
ALLOWED_CLIENTS = os.environ.get("ALLOWED_CLIENTS", "")
AUTHELIA_BASE = os.environ.get("AUTHELIA_BASE", "https://auth.example.com")
TARGET_URL = os.environ.get("TARGET_URL", "https://jellyfin.example.com/")
USERS_DB = Path(os.environ.get("USERS_DB", "/opt/authelia/config/users_database.yml"))
AUTHELIA_CONTAINER = os.environ.get("AUTHELIA_CONTAINER", "authelia")
MIN_PASSWORD_LENGTH = int(os.environ.get("MIN_PASSWORD_LENGTH", "8"))


def log(message: str) -> None:
    print(f"[{time.strftime('%Y-%m-%d %H:%M:%S')}] {message}", flush=True)


def parse_allowed_clients() -> list[ipaddress._BaseNetwork]:
    networks = []
    for raw in ALLOWED_CLIENTS.split(","):
        item = raw.strip()
        if not item:
            continue
        if "/" not in item:
            item = item + ("/32" if ":" not in item else "/128")
        networks.append(ipaddress.ip_network(item, strict=False))
    return networks


ALLOWED_NETWORKS = parse_allowed_clients()


def client_allowed(address: str) -> bool:
    if not ALLOWED_NETWORKS:
        return True
    ip = ipaddress.ip_address(address)
    return any(ip in network for network in ALLOWED_NETWORKS)


def json_response(handler: BaseHTTPRequestHandler, status: int, payload: dict) -> None:
    raw = json.dumps(payload).encode("utf-8")
    handler.send_response(status)
    handler.send_header("Content-Type", "application/json")
    handler.send_header("Content-Length", str(len(raw)))
    handler.end_headers()
    handler.wfile.write(raw)


def verify_bearer(handler: BaseHTTPRequestHandler) -> bool:
    if not HELPER_TOKEN:
        return True
    return handler.headers.get("Authorization", "") == f"Bearer {HELPER_TOKEN}"


def authelia_firstfactor(username: str, password: str) -> bool:
    payload = json.dumps(
        {
            "username": username,
            "password": password,
            "targetURL": TARGET_URL,
            "requestMethod": "GET",
            "keepMeLoggedIn": False,
        }
    )

    proc = subprocess.run(
        [
            "curl",
            "-sk",
            "--max-time",
            "20",
            "-o",
            "/dev/null",
            "-w",
            "%{http_code}",
            "-H",
            "Content-Type: application/json",
            "-d",
            payload,
            f"{AUTHELIA_BASE.rstrip('/')}/api/firstfactor",
        ],
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if proc.returncode != 0:
        raise RuntimeError(f"firstfactor curl failed: {proc.stderr.strip()[:200]}")
    return proc.stdout.strip() == "200"


def generate_argon2_hash(password: str) -> str:
    # Authelia v4.39 CLI has --password but no stdin password option. Running
    # this helper as a private locked-down service is mandatory because the
    # password may briefly be visible to privileged process inspection.
    proc = subprocess.run(
        [
            "docker",
            "exec",
            AUTHELIA_CONTAINER,
            "authelia",
            "crypto",
            "hash",
            "generate",
            "argon2",
            "--password",
            password,
        ],
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if proc.returncode != 0:
        raise RuntimeError(f"hash generation failed: {proc.stderr.strip()[:200]}")
    match = re.search(r"Digest:\s*(\S+)", proc.stdout)
    if not match:
        raise RuntimeError("hash generation produced no Digest line")
    return match.group(1)


def replace_user_password(username: str, digest: str) -> None:
    if not re.fullmatch(r"[A-Za-z0-9_.@-]{1,128}", username):
        raise ValueError("invalid username format")

    original = USERS_DB.read_text(encoding="utf-8").splitlines()
    out: list[str] = []
    in_user = False
    found_user = False
    replaced = False

    user_header = f"    {username}:"
    for line in original:
        if line.startswith("    ") and not line.startswith("        ") and line.rstrip().endswith(":"):
            in_user = line == user_header
            if in_user:
                found_user = True

        if in_user and line.startswith("        password:"):
            out.append(f"        password: {digest}")
            replaced = True
        else:
            out.append(line)

    if not found_user:
        raise ValueError("user not found in users_database.yml")
    if not replaced:
        raise ValueError("password field not found for user")

    backup = USERS_DB.with_suffix(USERS_DB.suffix + f".bak.helper-{username}.{int(time.time())}")
    shutil.copy2(USERS_DB, backup)

    fd, tmp_name = tempfile.mkstemp(prefix=USERS_DB.name + ".", dir=str(USERS_DB.parent))
    with os.fdopen(fd, "w", encoding="utf-8") as tmp:
        tmp.write("\n".join(out).rstrip() + "\n")
    os.replace(tmp_name, USERS_DB)
    log(f"updated password hash for user={username}; backup={backup.name}")


def restart_authelia() -> None:
    proc = subprocess.run(
        ["docker", "restart", AUTHELIA_CONTAINER],
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if proc.returncode != 0:
        raise RuntimeError(f"docker restart failed: {proc.stderr.strip()[:200]}")
    time.sleep(3)


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt: str, *args) -> None:  # noqa: A003
        log(f"{self.client_address[0]} {fmt % args}")

    def _precheck(self) -> bool:
        if not client_allowed(self.client_address[0]):
            json_response(self, 403, {"status": "KO", "message": "client not allowed"})
            return False
        return True

    def do_GET(self) -> None:  # noqa: N802
        if not self._precheck():
            return
        if self.path == "/health":
            if not verify_bearer(self):
                json_response(self, 403, {"status": "KO", "message": "forbidden"})
                return
            json_response(self, 200, {"status": "OK"})
            return
        json_response(self, 404, {"status": "KO", "message": "not found"})

    def do_POST(self) -> None:  # noqa: N802
        if not self._precheck():
            return
        if self.path != "/change-password":
            json_response(self, 404, {"status": "KO", "message": "not found"})
            return
        if not verify_bearer(self):
            json_response(self, 403, {"status": "KO", "message": "forbidden"})
            return

        try:
            length = int(self.headers.get("Content-Length", "0"))
            if length <= 0 or length > 16384:
                json_response(self, 400, {"status": "KO", "message": "invalid body length"})
                return
            body = json.loads(self.rfile.read(length).decode("utf-8"))
            username = str(body.get("username", ""))
            old_password = str(body.get("old_password", ""))
            new_password = str(body.get("new_password", ""))

            if not username or not old_password or not new_password:
                json_response(self, 400, {"status": "KO", "message": "username, old_password, and new_password are required"})
                return
            if len(new_password) < MIN_PASSWORD_LENGTH:
                json_response(self, 400, {"status": "KO", "message": f"new password must be at least {MIN_PASSWORD_LENGTH} characters"})
                return

            if not authelia_firstfactor(username, old_password):
                json_response(self, 401, {"status": "KO", "message": "invalid username or password"})
                return

            digest = generate_argon2_hash(new_password)
            replace_user_password(username, digest)
            restart_authelia()
            json_response(self, 200, {"status": "OK", "message": "password changed"})
        except Exception as exc:  # intentionally do not log credential values
            log(f"change-password failed: {type(exc).__name__}: {exc}")
            json_response(self, 500, {"status": "KO", "message": "internal helper error"})


def main() -> None:
    if not HELPER_TOKEN:
        log("WARNING: HELPER_TOKEN is empty; do not expose this service")
    log(f"allowed_clients={ALLOWED_CLIENTS or '<disabled>'}")
    server = ThreadingHTTPServer((BIND_HOST, PORT), Handler)
    log(f"listening on {BIND_HOST}:{PORT}; authelia_base={AUTHELIA_BASE}; users_db={USERS_DB}")
    server.serve_forever()


if __name__ == "__main__":
    sys.exit(main())
