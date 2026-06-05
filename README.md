# Authelia plugin for Jellyfin

[![License](https://img.shields.io/github/license/nikarh/jellyfin-plugin-authelia.svg)](https://github.com/nikarh/jellyfin-plugin-authelia)
[![GitHub Actions Build Status](https://github.com/nikarh/jellyfin-plugin-authelia/actions/workflows/release.yml/badge.svg)](https://github.com/nikarh/jellyfin-plugin-authelia/actions/workflows/release.yml)
[![Current Release](https://img.shields.io/github/release/nikarh/jellyfin-plugin-authelia.svg)](https://github.com/nikarh/jellyfin-plugin-authelia/releases)
[![Release RSS Feed](https://img.shields.io/badge/rss-releases-ffa500?logo=rss)](https://github.com/nikarh/jellyfin-plugin-authelia/releases.atom)
[![Main Commits RSS Feed](https://img.shields.io/badge/rss-commits-ffa500?logo=rss)](https://github.com/nikarh/jellyfin-plugin-authelia/commits/main.atom)


A plugin for Jellyfin that allows the use of [Authelia](https://www.authelia.com/) as an authentication and authorization backend.

## Description

Unlike the [SSO plugin](https://github.com/9p4/jellyfin-plugin-sso), this plugin uses [Authelia HTTP API](https://github.com/authelia/authelia/blob/master/api/openapi.yml).
The benefit of that approach is a native Jellyfin form and API for authentication. This means that any existing Jellyfin client should work with this plugin.
The main drawback of this approach is that only username+password authentication is supported (no 2FA).

The plugin will automatically create a new Jellyfin user upon successful authentication. Any valid Authelia user can log in to Jellyfin.

## Quick setup (easy)

### 1) Install plugin in Jellyfin

1. Go to **Dashboard -> Plugins -> Repositories**.
2. Add repository URL:
   - `https://raw.githubusercontent.com/nikarh/jellyfin-plugin-authelia/main/manifest.json`
3. Open **Catalog**, install **Authelia Authentication**.
4. Restart Jellyfin.

### 2) Configure plugin

Go to **Dashboard -> Plugins -> My Plugins -> Authelia Authentication** and fill:

- **Authelia Server**: your Authelia base URL, e.g. `https://auth.example.com`
- **Jellyfin Url**: the public URL users use for Jellyfin, e.g. `https://jellyfin-app.example.com`
- **Create a new user on successful login**: usually `enabled`
- **Authelia admin group name** (optional): e.g. `jellyfin-admins`

Then click **Save**.

> Note: this plugin does **not** use an Authelia API key.
> Authentication is done with each user's own username/password via Authelia HTTP endpoints.

### 3) Configure Authelia access rules

Make sure your Jellyfin app domain is allowed in Authelia access control, for example:

```yaml
access_control:
  rules:
    - domain: jellyfin-app.example.com
      policy: one_factor
      subject:
        - group:jellyfin-users
```

If you define `server.endpoints.authz`, ensure `auth-request` remains enabled:

```yaml
server:
  endpoints:
    authz:
      auth-request:
        implementation: 'AuthRequest'
```

### 4) Test login

Open Jellyfin, log in with an Authelia user that matches your access rule.

- If user auto-create is enabled, first successful login creates the Jellyfin user.
- If login fails with `403` in Authelia logs, usually the access-control rule (domain/subject/policy) is the cause.

## Password changes

This fork contains experimental support for native Jellyfin password changes for Authelia-backed users.

> Important: Authelia protects password changes with **Elevated Session** identity validation.
> For users who have not completed a second factor in Authelia, this usually means Authelia sends a One-Time Code to the user's configured email address.
> If your users have placeholder emails (for example `user@example.com`) or no working notifier, native Jellyfin password changes will not be user-friendly.

### Jellyfin <-> Authelia flow

1. Jellyfin validates `CurrentPw` by calling plugin `Authenticate(username, currentPassword)`.
2. In that same request flow, the plugin captures request-scoped credentials (in-memory only).
3. Jellyfin then calls plugin `ChangePassword(user, newPassword)`.
4. Plugin reuses the captured current password and calls Authelia:
   - `POST /api/firstfactor`
   - `POST /api/change-password` with `old_password` + `new_password`
5. If Authelia requires elevation, plugin starts elevation challenge and asks user to retry with one-time code.

### Elevation one-time code retry

When Authelia requires elevated session, Authelia sends a One-Time Code to the user's configured email address.
Retry password change in Jellyfin with this format in the **Current Password** field:

`currentPassword::otc=YOURCODE`

The plugin will:
- split `currentPassword` and `otc`
- call `PUT /api/user/session/elevation` with `{"otc":"YOURCODE"}`
- retry `POST /api/change-password`

### Important limitations

- 1FA-only users generally still need email One-Time Code elevation for password changes; their normal password alone is not enough.
- Jellyfin API does not expose a separate OTC field; this fork uses `::otc=` marker in the current password field.
- Credentials and OTC are request-scoped and ephemeral (not persisted intentionally).
- 2FA login itself is still not supported by this plugin (same as upstream design).
- A fully user-friendly password-change UX likely requires changes in Jellyfin Web or directing users to Authelia's own portal.

### Security considerations

- Passwords are transmitted to Authelia in JSON request bodies (`old_password`, `new_password`) as required by Authelia API.
- **Use HTTPS between Jellyfin and Authelia.** If `http://` is used on a shared network segment, credentials can be intercepted in transit.
- Avoid debug logging of raw credentials in reverse proxies, middleware, or packet captures.

### Password-change UX note

When Authelia requires elevation, Jellyfin currently has no dedicated OTC input field.
Use the **Current Password** field format:

`currentPassword::otc=YOURCODE`

Example:

`MyCurrentSecret123::otc=AB12CD34`

## Usage

1. Add `https://raw.githubusercontent.com/nikarh/jellyfin-plugin-authelia/main/manifest.json` as a new Jellyfin plugin repository
2. Install the `Authelia Authentication` plugin from the catalog
3. Configure the plugin by entering the URL of your Authelia server (can be either private or public), and a URL of your Jellyfin installation used in the Authelia `configuration.yml` rule.

## Authelia configuration

This plugin uses `/api/authz/auth-request` [endpoint](https://www.authelia.com/configuration/miscellaneous/server-endpoints-authz) for authentication.
If your Authelia `configuration.yml` file contains `server.endpoints.authz` section it [overrides](https://github.com/authelia/authelia/blob/eefd06e81b61a113269de3e38ae6ed7d096665ee/internal/configuration/validator/server.go#L122) the [defaults](https://github.com/authelia/authelia/blob/eefd06e81b61a113269de3e38ae6ed7d096665ee/internal/configuration/schema/server.go#L67), so you must explicitly enable `auth-request` endpoints:

```yaml
server:
    endpoints:
        authz:
            auth-request:
                implementation: 'AuthRequest'
```

## License

All files in this repository excluding the [Authelia logo](./logo.png) are licensed under an [MIT](./LICENSE) license.

The [Authelia logo](./logo.png) in this repository is a modified version of the [Authelia title logo](https://www.authelia.com/images/branding/title.svg) with added paddings and a background, rasterized as a PNG, and is licensed under the [Apache 2.0](https://github.com/authelia/authelia/blob/master/LICENSE) license (see [Authelia branding guide](https://www.authelia.com/reference/guides/branding/)).

