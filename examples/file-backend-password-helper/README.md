# Authelia file-backend password helper

This optional helper is a pragmatic workaround for homelab Authelia **file backend** setups where users do not have deliverable email addresses for Authelia Elevated Session One-Time Codes.

It lets the Jellyfin plugin change a user's Authelia password without using Authelia's `/api/change-password` endpoint.

## Flow

1. User opens Jellyfin's normal password change dialog.
2. Jellyfin verifies the current password using this plugin.
3. Plugin calls the helper with `username`, `old_password`, and `new_password`.
4. Helper verifies `old_password` against Authelia `/api/firstfactor`.
5. Helper writes a new Argon2id hash to `users_database.yml`.
6. Helper restarts Authelia so the file backend reloads.

Only the password hash is changed. Groups, email, TOTP, WebAuthn, Duo, and other Authelia DB records are not modified.

## Security model

This helper is privileged. It can change Authelia file-backend passwords.

Use **all** of these protections:

- bind to a private address only
- set a strong `HELPER_TOKEN`
- restrict `ALLOWED_CLIENTS` to Jellyfin's IP or subnet
- firewall the port so only Jellyfin can connect
- never expose this helper directly to the Internet
- use HTTPS or an isolated private network if traffic can be observed

The bearer token is a shared secret. Jellyfin sends it as:

```text
Authorization: Bearer YOUR_TOKEN
```

The plugin stores the same value in **Password change helper bearer token**.

## Install

Run on the Authelia host/container host:

```bash
sudo HELPER_TOKEN='replace-with-long-random-secret' \
  BIND_HOST='0.0.0.0' \
  PORT='9944' \
  ALLOWED_CLIENTS='192.168.20.51' \
  AUTHELIA_BASE='https://auth.example.com' \
  TARGET_URL='https://jellyfin-app.example.com/' \
  USERS_DB='/opt/authelia/config/users_database.yml' \
  AUTHELIA_CONTAINER='authelia' \
  ./install.sh
```

If `HELPER_TOKEN` is omitted, `install.sh` generates one and prints it once.

## Jellyfin plugin config

Set:

- **Password change helper URL**: `http://AUTHELIA_PRIVATE_IP:9944`
- **Password change helper bearer token**: same token as `HELPER_TOKEN`

Leave these fields empty to use Authelia's native password-change API instead.

## Uninstall

```bash
sudo ./uninstall.sh
```

Keep the environment file for inspection:

```bash
sudo KEEP_ENV=true ./uninstall.sh
```

## Notes

- This helper is only for Authelia's file backend.
- For LDAP or SQL-backed user providers, implement password changes in that backend instead.
- This helper intentionally does not implement registration, password reset, or second-factor management.
