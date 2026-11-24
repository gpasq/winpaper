# Paperboy for Windows

A Windows system tray app that shows Bing homepage images, from today's back to 2010, and sets any of them as your desktop wallpaper (in 4K where available). It's a port of the macOS menu bar app in `../paperboy`.

## Build & run

    pwsh build.ps1            # builds build\Paperboy.exe
    pwsh build.ps1 -Install   # also copies it to %LOCALAPPDATA%\Programs\Paperboy and launches it

Building requires the .NET SDK 8 or newer (`scoop install dotnet-sdk` or `winget install Microsoft.DotNet.SDK.8`). Running requires the .NET 8 or newer Desktop Runtime. The app rolls forward to whatever newer version is installed.

## Installer (Windows ARM64)

    pwsh build-installer.ps1  # builds build\Paperboy-<version>-arm64-setup.exe

The installer bundles the .NET runtime, so the target PC doesn't need .NET. It installs per-user into `%LOCALAPPDATA%\Programs\Paperboy` with no admin prompt. It adds a Start menu shortcut and an optional "Launch Paperboy when I sign in" checkbox, and shows up in Settings → Apps for uninstalling. Uninstalling removes the login item and the app's settings, but leaves downloaded images in `Pictures\Paperboy`. Building it requires Inno Setup 6.3+ (`scoop install extras/inno-setup`). The version comes from `<Version>` in `Paperboy.csproj`.

Silent install: `Paperboy-1.0.0-arm64-setup.exe /VERYSILENT /TASKS=startup` (leave out `/TASKS=startup` to skip the login item).

## Using it

Click the tray icon to open the popup, and click anywhere else (or press Esc) to close it. Right-click the icon for Open, Refresh and Quit. Starting the app a second time opens the popup of the copy that's already running.

## Features

- **Recent** tab: thumbnail grid of the last ~15 days of Bing images with title and caption
- **Archive** tab: browse every Bing image back to 2010 by year and month, or hit shuffle for a random day
- **Set as Wallpaper** downloads the full-size image and applies it, filling every display
- **Download** saves images to `%USERPROFILE%\Pictures\Paperboy`
- **Automatically use today's image**: checks hourly and switches when Bing posts a new one
- **Launch at login**: adds the app to `HKCU\...\CurrentVersion\Run`. It works best once installed with `-Install`.

Settings are stored in `HKCU\Software\Paperboy`. The popup follows the Windows light/dark app theme and accent color.

## Archive

Bing's own API only serves about the last 15 days, so the Archive tab loads one year at a time from the community-run [Bing Wallpaper Archive](https://github.com/npanuhin/Bing-Wallpaper-Archive) (bing.npanuhin.me). It uses your region's archive when one exists and falls back to the US archive otherwise.

Images from 2019 onward come straight from Bing in UHD. Older ones are only available at their original resolution, which can be as low as 958×512 for 2010, so they may look soft on large displays.

## Icon

`Resources/AppIcon.ico` is generated from `Resources/AppIcon.png` (the macOS app's icon) by `pwsh scripts/make-icon.ps1`.
