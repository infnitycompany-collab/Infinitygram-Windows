# INFINITYGRAM Windows

Telegram multi-account userbot control plane — Windows desktop app (Avalonia, .NET 8).

## Build

```bash
dotnet build Infinitygram.Windows.sln -c Release
dotnet run --project tests/Infinitygram.Runner -c Release
```

## Publish (Windows, self-contained single file)

```bash
dotnet publish src/Infinitygram.App/Infinitygram.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/win-x64
```

Extract the whole output folder on Windows and run `Infinitygram.App.exe` from
inside its folder (it needs the native DLLs next to it). Do not copy the exe alone.

## Install script

On Windows: `powershell -ExecutionPolicy Bypass -File dist/install.ps1`

## Security rules

- `api_hash` is vault-sealed (AES-256-GCM, PBKDF2 600k + HKDF) and never
  displayed or logged. Secrets never go into git, logs, or reports.
- Live Telegram login requires the owner's own api_id/api_hash and a
  phone/OTP/2FA/QR login on his own Windows machine. Until then, live
  operations fail fast with a clear "not configured" message — never fake results.
- No Dragon name, logo, or code is used. The UI layout language is original
  work in INFINITY branding (navy #0A1B33 + gold #D4A017).

## Status

- Release build: 0 warnings / 0 errors
- Tests: 49/49 (reflection runner — same xUnit tests, vstest hangs on the dev VM)
- UI: 17 modules, standard 1280×720 window, sidebar 220 + header 56 + status bar
- Live adapter: skeleton only — wiring needs the owner's credentials + live testing
