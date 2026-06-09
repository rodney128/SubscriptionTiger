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
- Confirm manual entry fields are hidden by default
- Tap Add Manual Subscription and confirm manual form expands
- Tap Cancel Manual Entry and confirm manual form collapses
- Confirm Scan Sources section shows status rows for Gmail/Outlook/Other email/Bank file
- Tap Gmail row and confirm it does not open a browser OAuth flow
- Confirm Gmail pending message appears in-app
- Tap Outlook, Other email, and Bank file rows and confirm each shows a short coming-soon message
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
- Manual entry should stay collapsed until explicitly opened.
- Scan source statuses should look informational first, not like fully active scan buttons.
