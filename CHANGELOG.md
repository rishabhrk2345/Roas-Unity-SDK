# Changelog

## 0.1.0

Initial release. Unity port of the ROASSensor Android/iOS SDK contract:

- Install reporting, session tracking (30-min idle + local-midnight, on-device sequencing,
  foreground-time accumulation), identity binding, PII hashing (email/phone, byte-parity with
  the backend and every other SDK), funnel events, fast purchase verification, deep-link
  forwarding.
- HMAC-SHA256 beacon signing with server-clock-offset self-correction.
- Offline-durable delivery queue (`PlayerPrefs`-backed).
- Android: advertising id (GAID), App Set Id, Play Install Referrer, root/emulator signals.
- iOS: IDFA, IDFV, App Tracking Transparency, Apple Search Ads token, SKAdNetwork /
  AdAttributionKit conversion values, jailbreak signals.
- Editor tooling: settings asset creation, automatic Xcode framework linking, EDM4U Android
  dependency resolution.

Not yet ported: Vivo/Huawei/Xiaomi/Samsung OEM install-referrer fallbacks.
