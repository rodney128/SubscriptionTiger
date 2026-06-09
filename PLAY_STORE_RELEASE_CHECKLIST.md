# SubscriptionTiger Play Store Release Checklist

## App Name
SubscriptionTiger

## Package Name
com.farenoughnorth.subscriptiontiger

## Release Version
0.1.0

## Version Code
1

## Short Description
Find and manage recurring subscriptions from supported email and financial sources.

## Full Description
SubscriptionTiger helps identify possible recurring subscriptions, review suspected subscriptions, confirm real subscriptions, and manage a clean list of recurring charges.

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
SubscriptionTiger may process email or financial-file information to detect recurring subscriptions.

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
