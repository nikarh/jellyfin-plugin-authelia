# Authelia plugin for Jellyfin

[![License](https://img.shields.io/github/license/nikarh/jellyfin-plugin-authelia.svg)](https://github.com/nikarh/jellyfin-plugin-authelia)
[![GitHub Actions Build Status](https://github.com/nikarh/jellyfin-plugin-authelia/actions/workflows/release.yml/badge.svg)](https://github.com/nikarh/jellyfin-plugin-authelia/actions/workflows/dotnet.yml)
[![Current Release](https://img.shields.io/github/release/nikarh/jellyfin-plugin-authelia.svg)](https://github.com/nikarh/jellyfin-plugin-authelia/releases)
[![Release RSS Feed](https://img.shields.io/badge/rss-releases-ffa500?logo=rss)](https://github.com/nikarh/jellyfin-plugin-authelia/releases.atom)
[![Main Commits RSS Feed](https://img.shields.io/badge/rss-commits-ffa500?logo=rss)](https://github.com/nikarh/jellyfin-plugin-authelia/commits/main.atom)


A plugin for Jellyfin that allows using [Authelia](https://www.authelia.com/) as an authentication and authorization backend.

## 🤔 Description

Unlike [SSO plugin](https://github.com/nikarh/jellyfin-plugin-authelia), this plugin uses [Authelia HTTP API](https://github.com/authelia/authelia/blob/master/api/openapi.yml).
The benefit of that approach is a native Jellyfin form and API for authentication. This means that any existing Jellyfin client should work with this plugin/
The drawback of this approach though is that only username+password authentication is supported (no 2FA).

The plugin will automatically create a new Jellyfin user upon successful authentication. Any valid Authelia user can login to Jellyfin.

## 🏗️ Usage

1. Add `https://raw.githubusercontent.com/nikarh/jellyfin-plugin-authelia/main/manifest.json` as a new Jellyfin plugin repository
2. Install `Authelia Authentication` plugin from catalog
3. Configure the plugin by entering the URL of your Authelia server (can be either private or public), and a URL of your Jellyfin installation used in Authelia `configuration.yml` rule.
