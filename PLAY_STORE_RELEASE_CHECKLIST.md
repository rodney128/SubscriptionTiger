# Subscription Tiger Play Store Release Checklist

## App Name
Subscription Tiger

## Package Name
com.farenoughnorth.subscriptiontiger

## Release Version
0.1.0

## Version Code
1

## Short Description
Find and manage recurring subscriptions from supported email and financial sources.

## Full Description
Subscription Tiger helps identify possible recurring subscriptions, review suspected subscriptions, confirm real subscriptions, and manage a clean list of recurring charges.

This is an early release intended for testing and validation.

## Current Features
- Review suspected subscriptions
- Save or dismiss suspected subscriptions
- Manage confirmed subscriptions
- Local subscription storage
- Gmail scan foundation
- Outlook scan foundation
- Other email scan foundation
- Bank file scan foundation
- Diagnostics available under More Options

## Privacy Notes
Subscription Tiger may process email or financial-file information to detect recurring subscriptions.

Before public release, confirm:
- What data is read
- Whether data leaves the device
- Whether email data is stored
- Whether financial file data is stored
- Whether analytics/crash reporting is used
- Whether any third-party services receive user data

## Required Before Production Release
- Hosted privacy policy URL
- Google Play Data Safety answers
- App screenshots
- Feature graphic
- App category
- Contact email
- Content rating questionnaire
- Target audience selection
- Closed testing track, if required by the developer account
- Real Android device/emulator visual test

## Debug-Mode Upload Resolution
- Play Console upload initially reported a debug-mode issue.
- Release outputs were cleaned and regenerated from a fresh Release build and publish.
- Final AAB path: `C:\Users\conta\source\repos\SubscriptionTiger\SubscriptionTiger\bin\Release\net10.0-android\publish\com.farenoughnorth.subscriptiontiger-Signed.aab`
- Generated Release manifest debuggable status: `android:debuggable=\"true\"` not found in checked release manifests.
- Signing verification result: `jarsigner -verify` passes and reports signer `CN=SubscriptionTiger, OU=Mobile, O=SubscriptionTiger, L=NA, ST=NA, C=US`.

## Gmail OAuth Status
- Gmail OAuth is working end-to-end in Debug on Android test device.
- OAuth Android client package: `com.farenoughnorth.subscriptiontiger`.
- OAuth Android debug SHA-1: `D9:44:22:24:C9:C7:2D:AE:A8:DF:5D:3C:B3:40:B1:27:5E:40:D6:06`.
- OAuth client ID: `449735589472-shqeavauf9mrhn0o92khgif14s7hm9j0.apps.googleusercontent.com`.
- OAuth redirect URI: `com.googleusercontent.apps.449735589472-shqeavauf9mrhn0o92khgif14s7hm9j0:/oauth2redirect`.
- Android callback scheme/path: `com.googleusercontent.apps.449735589472-shqeavauf9mrhn0o92khgif14s7hm9j0` + `/oauth2redirect`.
- Google branding shown in flow: `SubscriptionTiger by Far Enough North`.
- Gmail scan is enabled for internal OAuth validation builds.
- Subscription review workflow remains testable through sample/manual/local data.

## Play Upload Signing Key Requirement
- Play Console currently expects upload key SHA-1 `EE:37:79:B4:52:62:F3:EF:C9:C3:7B:26:E8:F7:99:22:38:6F:1C:1C` for this app slot.
- Do not regenerate signing keystores for this existing Play app entry.
- Final upload AAB must verify with the expected signer before upload.

## Upload Key Reset (received via Play Console)
- Status: Upload key reset request received and accepted by Google Play.
- App: Subscription Tiger
- Package: com.farenoughnorth.subscriptiontiger
- New upload key becomes valid on: Jun 12, 2026 at 1:31 AM UTC.
- The previous upload key SHA-1 `EE:37:79:B4:52:62:F3:EF:C9:C3:7B:26:E8:F7:99:22:38:6F:1C:1C` is superseded once the new key becomes valid.
- New upload certificate fingerprints expected by Play (public identity values, not secrets):
  - MD5: `1F:79:DD:D4:5D:09:E1:83:1E:DB:A2:93:5E:0D:C3:EA`
  - SHA-1: `2B:7F:6B:84:24:D2:84:F1:C6:07:F2:AF:B8:1B:D1:61:19:0A:CD:B4`
- This is a signing-identity change only. It is not missing app code.
- Future AAB uploads must be signed with the local upload keystore whose certificate SHA-1 matches the new `2B:7F...` value above.

### Later: verify local upload keystore SHA-1 matches the new key
- Local upload keystore (not tracked in Git, ignored via `_private_signing/`): `_private_signing\subscriptiontiger-release.keystore`
- Run this when preparing the next release (it prompts for the keystore password interactively; do not pass passwords on the command line, and do not paste them anywhere):
  - `keytool -list -v -keystore "_private_signing\subscriptiontiger-release.keystore" -alias $env:SUBTIGER_ANDROID_KEY_ALIAS`
- In the output, find the `SHA1:` (or `SHA-1:`) line and confirm it equals:
  - `2B:7F:6B:84:24:D2:84:F1:C6:07:F2:AF:B8:1B:D1:61:19:0A:CD:B4`
- If it matches: the local keystore is the correct new upload key; proceed to sign the Release AAB with the existing environment-variable signing config.
- If it does NOT match: stop. Do not upload. The local keystore is not the reset upload key, and the correct keystore/alias must be located first.
