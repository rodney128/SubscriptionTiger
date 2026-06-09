# SubscriptionTiger Internal Test Notes

## Test Goal
Verify that SubscriptionTiger installs, launches, displays the cleaned-up main screen, and allows basic subscription review workflow testing.

## Minimum Smoke Test
- Install app
- Launch app
- Confirm splash screen appears
- Confirm main screen appears
- Confirm tiger icon appears
- Confirm Subscription Review section is visible near the top
- Confirm Add Sample Subscription and Add Manual Subscription actions are easy to find
- Confirm Scan Sources section labels Gmail as pending and other sources as coming soon
- Tap Scan Gmail and confirm it does not open a browser OAuth flow
- Confirm Gmail status message explains OAuth is pending for this internal test build
- Confirm Last Activity card stays compact and readable
- Tap More Options
- Confirm More Options content is collapsed by default until expanded
- Confirm Help can show/hide
- Confirm Diagnostics can show/hide
- Confirm suspected subscriptions render
- Confirm a suspected subscription can be saved or dismissed
- Confirm confirmed subscriptions render
- Confirm confirmed subscription delete still works
- Confirm bottom buttons are not blocked by Android navigation

## Tester Feedback Requested
- Did the app install successfully?
- Did the app launch without crashing?
- Was the main screen understandable?
- Were the buttons easy to find?
- Did anything look cut off or broken?
- Did any action crash?
- What phone model and Android version were used?

## Gmail OAuth Internal-Testing Note
- Gmail sign-in is intentionally disabled/pending in this internal-test build while native Google authorization setup is completed.
- Test app launch, main screen, More Options, sample/manual subscription flow, suspected/confirmed subscription handling, and diagnostics.
- Do not report Gmail OAuth disabled as a crash if the app shows the expected message.

## UI Debug Focus
- Main flow should be understandable in a few seconds: add sample/manual data, review suspected subscriptions, confirm/dismiss, and manage confirmed subscriptions.
- Diagnostics and developer details should stay secondary under More Options.
