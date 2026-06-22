# Mangrove Android app

Native **Kotlin + Jetpack Compose** client for a Mangrove server (spec §11).

Stack: Compose (Material 3), Navigation-Compose, Retrofit + OkHttp + kotlinx-serialization,
Coil (authenticated image loading/caching). Target/compile SDK 35, min SDK 26.

## Status (milestone 1 — core reading flow)

Implemented:

- **Server picker** — enter your server URL (e.g. `http://192.168.0.10:5000`), saved on device.
- **Login** — username/password against `/api/auth/login`.
- **Sessions that persist** — the refresh cookie is stored in a persistent OkHttp cookie jar and
  an OkHttp `Authenticator` silently calls `/api/auth/refresh` on `401`, so you stay signed in.
- **Home** — libraries, "Continue reading", and "Recently added" rails.
- **Library grid** — covers in an adaptive grid (uses server-side covers, incl. `folder.jpg`).
- **Series detail** — cover, metadata, and a flat chapter list.
- **Image reader** — Compose `HorizontalPager` with paged LTR/RTL, fit-to-screen, page indicator,
  and progress sync to `/api/progress`.
- **Reading direction is a synced per-user preference** (`reader.dir` via `/api/me/preferences`),
  so it matches the web reader across devices.
- **Offline downloads** — download a single chapter or a whole series ("Download all"). Page images
  are saved on the device (with a small metadata file per chapter), downloads resume if interrupted,
  and a **Downloads** screen lets you browse and read saved series with **no connection** (reachable
  from Home, or via "Open offline downloads" on the login screen). The reader automatically uses
  local pages when a chapter is downloaded.

Not yet implemented (planned next): webtoon continuous scroll, dual-page, tap-zone navigation,
EPUB reader, true background downloads (WorkManager) + Wi-Fi-only option, favorites/notifications,
multi-server, encrypted token storage, and a settings screen.

## Prerequisites

- **JDK 17** (the build is pinned to Java 17).
- **Android SDK** with platform 35 and build-tools 35. Easiest via Android Studio.
- A `local.properties` in this folder pointing at the SDK (git-ignored). Example:

  ```properties
  sdk.dir=C:/Users/<you>/AppData/Local/Android/Sdk
  ```

  Android Studio creates this automatically when you open the project.

## Build the APK

```bash
cd apps/android
./gradlew assembleDebug        # Windows: .\gradlew.bat assembleDebug
# -> app/build/outputs/apk/debug/app-debug.apk   (sideload-ready)
```

Install on a connected device/emulator:

```bash
adb install -r app/build/outputs/apk/debug/app-debug.apk
```

Or just copy `app-debug.apk` to the phone and tap it (enable "install from unknown sources").

> The app allows cleartext HTTP so it can reach LAN servers by IP. Put the server behind HTTPS
> for remote/internet use.

## Release / signed APK

```bash
./gradlew assembleRelease      # configure signing first (see spec §11)
```

Generate a keystore once and provide its details via env vars referenced from
`app/build.gradle.kts` — never commit the keystore.

## Open in Android Studio

`File ▸ Open…` → select `apps/android`. Studio will sync Gradle (wrapper pinned to 8.12.1) and
let you run on an emulator or device.
