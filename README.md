# SonNuage Android

**English** · [Français](README.fr.md)

Android app for [SonNuage](https://github.com/SonNuage/sonnuage), a self-hosted music server:
.NET MAUI Blazor Hybrid (same pages as the web client) and a Media3 playback service (notification, Android Auto).

Status: works, used every day. The project is young and still in development.

| Folder | Role |
|---|---|
| `src/SonNuage.Mobile` | App: Blazor host, Media3 player, Android Auto, secure storage of the accounts |
| `build/` | Build image (Android SDK + MAUI workload) and APK script without Android Studio |

The pages and components come from the `SonNuage.UI` library of the
[sonnuage](https://github.com/SonNuage/sonnuage) repository:

- **in CI**: NuGet package published by the CI of `sonnuage` (version in `Directory.Packages.props`)
- **in development**: if `sonnuage` is cloned next to this repository (`../sonnuage`), its sources are used
  directly (see `Directory.Build.props`, property `SonNuageRepo`).

To follow a new server version, raise `SonNuage.UI` in `Directory.Packages.props` to the published
version (`X.Y.Z` for a server tag, `0.1.0-ci.N` for a build of `main`).

## APK without Android Studio

```bash
docker build -f build/android-sdk.Dockerfile -t sonnuage-android-sdk build
```

```bash
docker run --rm -v "$PWD:/host:ro" -v "$PWD/../sonnuage:/sonnuage-src:ro" -v "$PWD/out:/out" -v sonnuage-nuget:/root/.nuget/packages sonnuage-android-sdk sh /host/build/build-apk.sh Release
```

A single file is produced: `out/sonnuage-release.apk`, signed and installable. `dotnet publish` also generates an
**unsigned** `org.pmjb.sonnuage.apk`: it is an intermediate file, Android refuses to install it, so it is not
copied. Without a keystore, the local APK is signed with the image's debug key: to update over an APK
from the CI (another key), the app must be uninstalled first.

## How it works

- **Accounts**: on first launch, the app asks for the server address, then the login. Several
  accounts (one user on one server, possibly several servers) can be signed in at the same time.
  **Settings › Accounts** shows the active account, and lets you add one, change the active account or sign
  out of a given account. The active account sets the library, playback, Android Auto and the notification.
  Changing it stops playback and reloads the saved queue of the other account.
- Each account has its own tokens (SecureStorage, renewed one by one with a lock per account), its
  queue, its playback settings and its entries in the audio cache. An earlier single-account install is
  migrated automatically into a first account (session, queue and settings included).
- **Crashes**: any unhandled exception (.NET, tasks, Java) is written to `crash.log` (the app's private
  folder) and to logcat (`adb logcat -s SonNuage`). On the next launch, a native screen shows the report with a
  "Copy the report" button. If startup itself fails, this screen replaces the interface.

## GitLab CI/CD

[.gitlab-ci.yml](.gitlab-ci.yml): build image (when its recipe changes) → signed APK as an artifact
(`dist/sonnuage-X.Y.Z.apk`, signature checked by `apksigner`) → on `main`, semantic-release creates the `vX.Y.Z` tag
and its release, to which the tag's pipeline attaches the APK (generic package registry).

Signing uses the masked variables `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`,
`ANDROID_KEY_ALIAS` and `ANDROID_KEY_PASSWORD`. They hold the key that already signed the APKs: another key
would force users to uninstall the app before updating. A new keystore is created with:

```bash
keytool -genkeypair -v -keystore sonnuage.keystore -alias sonnuage -keyalg RSA -keysize 4096 -validity 10000
```

### Versions

Versions are published by [semantic-release](https://semantic-release.gitbook.io/) ([.releaserc.json](.releaserc.json)),
from the commit messages in the [Conventional Commits](https://www.conventionalcommits.org/en/) format:
`fix: …` gives a patch (1.0.1), `feat: …` a minor version (1.1.0), `feat!: …` or a
`BREAKING CHANGE:` footer a major version (2.0.0). `docs`, `chore`, `ci`, `refactor`, `test` publish nothing.
On `main`, the `semantic-release` job creates the `vX.Y.Z` tag and the GitLab release with its notes. The tag's
pipeline then publishes the version. It uses `GITLAB_TOKEN`, a token of the `sonnuage` group (masked and protected variable,
protected `v*` tags).

### GitHub Actions

[.github/workflows/ci.yml](.github/workflows/ci.yml) produces the same signed APK on GitHub. It checks out the
`sonnuage` repository of the same owner next to this one and builds against its sources, without a NuGet registry. Secrets to
define: the four signing variables above, plus `SONNUAGE_REPO_TOKEN` (read token) if the `sonnuage` repository
is private. On a `vX.Y.Z` tag, the APK is attached to a GitHub release.

## Licence

SonNuage is free software under the [GNU AGPL v3.0 or later](LICENSE) licence: anyone can use it,
study it, modify it and redistribute it. A modified version, including one offered as an online service, must
publish its source code under the same licence.

Copyright © 2026 pmj.bailly and the SonNuage contributors.
