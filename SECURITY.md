# Security Policy

YATSS is prerelease software with no production track installations. Security
and safety reports are still welcome, especially when they affect local files,
controller communication, firmware update behavior, reports, logs, backups, or
track-power control.

## Supported Versions

Only the current `master` branch is reviewed for security fixes. Public beta
tags are snapshots for evaluation and may not receive separate patch releases.

## Reporting A Vulnerability

Please report suspected vulnerabilities privately through GitHub's private
vulnerability reporting for this repository, if available. If that option is
not available, open a GitHub issue with only a high-level description and ask
for a private contact path before sharing exploit details, logs, databases, or
local paths.

Include:

- YATSS version, tag, or commit.
- Windows or Wine environment.
- Controller board profile and firmware version, if relevant.
- Clear reproduction steps.
- The impact you believe is possible.

Do not include race databases, generated reports, credentials, personal racer
data, or full serial logs in public issues.

## Hardware Safety

If a report involves track power, relay polarity, watchdog behavior, or
controller reset behavior, disconnect the track from live race use until the
behavior is understood. Software cannot make unsafe wiring safe by itself.
