<p align="center">
  <img src="Assets/app.ico" width="96" alt="Zenith Launcher icon"/>
</p>

<h1 align="center">Zenith Launcher</h1>

<p align="center">
  A modern, open-source Minecraft launcher built with Avalonia UI.<br/>
  Supports Microsoft, Ely.by, and offline accounts with built-in mod management.
</p>

---

## Features

- **Multi-account support** — Microsoft, Ely.by, and offline profiles with encrypted token storage (DPAPI)
- **Instance management** — Create, clone, rename, and configure Minecraft instances with per-instance profiles
- **Built-in mod browser** — Search, download, and install mods directly from Modrinth
- **Modpack browser** — Install CurseForge and Modrinth modpacks with one click
- **Resource & shader packs** — Browse and manage resource packs, shader packs, and data packs
- **Mod loader support** — Automatic installation of Fabric, Forge, NeoForge, Quilt, and OptiFine
- **Java management** — Auto-detect, download (Adoptium/Temurin), and configure Java runtimes
- **Server management** — Import and edit your multiplayer server list (`servers.dat`)
- **Discord Rich Presence** — Shows what you're playing, browsing, or editing in real time
- **Crash analysis** — Automatic crash log analysis with human-readable summaries
- **6 languages** — English, Russian, German, Spanish, French, Japanese
- **Customizable icons** — Pick from built-in icon set or use your own images
- **Dark theme** — Polished Fluent-style dark UI with accent color support

## Screenshots

> _Screenshots coming soon. Build and run the launcher to see it in action._

## Requirements

- **OS:** Windows 10/11 (x64)
- **SDK:** [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- **Runtime:** Java 8–24 (auto-downloaded if missing)

## Building from source

```bash
# Clone the repository
git clone https://github.com/MrGiperTroll/Zenith-Launcher.git
cd Zenith-Launcher

# Build in Release mode
dotnet build -c Release

# Run the launcher
dotnet run -c Release
```

The built executable will be at:
```
bin\Release\net10.0-windows\Zenith Launcher.exe
```

### Self-test

Verify Discord Rich Presence initialization:

```bash
"bin\Release\net10.0-windows\Zenith Launcher.exe" --rpc-selftest
# Expected output: RPC-SELFTEST RESULT: PASS
```

## Project structure

```
CustomMcLauncher/
├── Assets/              # Icons, images, app manifest
├── Extensions/          # XAML markup extensions (L10n binding)
├── Models/              # Data models (Instance, Account, Modrinth, etc.)
├── Services/            # Core services (auth, launch, Discord, mods, settings)
├── ViewModels/          # MVVM view models
├── Views/               # Avalonia AXAML windows and controls
├── Converters/          # XAML value converters
└── Program.cs           # Entry point
```

## Security

- OAuth tokens are encrypted at rest using Windows DPAPI (`ProtectedData`)
- Access tokens are redacted in all log output
- No telemetry, analytics, or phone-home code
- All external connections use HTTPS
- Account data stored locally at `%APPDATA%\.zenith\`

See the full security audit in the commit history for details.

## Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/my-feature`)
3. Commit your changes
4. Push to the branch (`git push origin feature/my-feature`)
5. Open a Pull Request

### Code style

- Follow existing conventions (namespace-scoped imports, `ObservableProperty` fields prefixed with `_`)
- Keep builds clean: 0 warnings, 0 errors (`dotnet build -c Release`)
- Localization: add new strings to all 6 language files in `Services/L10n.cs`

## License

This project is open source. See the [LICENSE](LICENSE) file for details.

## Acknowledgments

- [Avalonia UI](https://avaloniaui.net/) — Cross-platform UI framework
- [CmlLib](https://github.com/CmlLib/CmlLib) — Minecraft launcher core library
- [DiscordRPC](https://github.com/discord-net/DiscordRichPresence) — Discord Rich Presence integration
- [Modrinth API](https://docs.modrinth.com/) — Mod browsing and download API
