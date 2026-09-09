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

## Username case sensitivity

Jellyfin 12.0 treats usernames as **case-insensitive**: `Alice` and `alice` are the same Jellyfin account, and two users that differ only by capitalization cannot exist.

Authelia treats usernames as **case-sensitive**. Jellyfin stores each user under the casing they were created with, so the plugin authenticates using that stored username rather than whatever was typed:

1. The first login must use the exact casing Authelia expects. It creates the Jellyfin user (when enabled) under that same casing.
2. Every later login reuses it. Once Jellyfin holds `test`, signing in as `TEST`, `Test` or `test` all authenticate against Authelia as `test`.

A Jellyfin user's name is therefore the Authelia identity it logs in as, and renaming one in Jellyfin re-points it at the Authelia user of that name.

Two Authelia users whose names differ only by case cannot have separate Jellyfin accounts, since Jellyfin itself cannot store both.

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

