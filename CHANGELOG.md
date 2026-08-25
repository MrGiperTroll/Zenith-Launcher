# Changelog

All notable changes to Zenith Launcher will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.2] - 2025-08-25

### Changed
- Simplified update notification window to minimal two-button UI
- Silent installer update flow (`/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS`)
- Added localized update strings for all 6 supported languages

### Fixed
- Discord Rich Presence button now links to correct GitHub releases page
- Update installer now uses correct versioned filename

## [1.0.1] - 2025-08-25

### Fixed
- Corrected clone repository URL in README.md

### Changed
- Updated documentation and project metadata

## [1.0.0] - 2025-08-25

### Added

#### Core Launcher
- Modern Fluent Dark UI built with Avalonia 12.1
- Instance management: create, rename, delete Minecraft instances
- Automatic Minecraft version detection and download
- Java version detection and management
- Crash report analysis and diagnostics

#### Mod & Modpack Support
- Modrinth integration: browse, search, and install mods directly in the launcher
- Modpack browser with one-click install
- Fabric, Forge, NeoForge, Quilt, and OptiFine loader support
- Mod metadata reader and content browser

#### Multi-Account Authentication
- Microsoft OAuth2 authentication with device code flow
- Ely.by account support for alternative authentication
- Offline account mode
- DPAPI-encrypted token storage for all credentials
- Account switching and profile management

#### Discord Integration
- Rich Presence with activity states (Browsing, Installing, Configuring, Launching, etc.)
- Event-driven IPC with watchdog and zombie-pipe detection
- Dynamic status updates reflecting current launcher activity
- Clickable "Download Launcher" button in Discord profile

#### Auto-Update
- Background update check against GitHub Releases API
- Semver comparison with download progress UI
- One-click download and install flow from in-app notification

#### Internationalization
- Full localization support for 6 languages:
  - English, Russian, German, Spanish, French, Japanese
- Runtime language switching without restart

#### Installer
- Inno Setup 6 installer with built-in dark mode (`WizardStyle=dark`)
- Per-user installation (no admin required)
- Desktop and Start Menu shortcuts
- Auto-close running launcher before install

#### Security
- DPAPI encryption for all stored tokens and secrets
- No hardcoded credentials or telemetry
- OAuth CSRF state validation
- HTTPS-only external connections
- Access tokens redacted from all log output

**Full Changelog**: https://github.com/MrGiperTroll/Zenith-Launcher/commits/v1.0.0
