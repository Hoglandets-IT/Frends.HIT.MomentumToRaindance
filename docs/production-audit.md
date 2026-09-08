# Production audit — 2026-09-08

The local audit and remediation pass is complete. This is not a record of deployment or Raindance production acceptance.

## Changes made

| Area | Outcome |
| --- | --- |
| Frends task design | Kept two discoverable tasks: Fetch and Convert, allowing intermediate JSON processing. Checked conventions against the reference RemoteFS module and Frends documentation. |
| Checkpoint | Added `LastLocalId` to both results and Convert input. Fetch preserves the input. Convert excludes stale nodes, rejects invalid/duplicate pending IDs, and advances only after a complete successful conversion with invoice output. No-work results preserve the input. |
| HTTP and credentials | Removed certificate-validation bypass, retained HTTPS validation, pooled clients, disposed requests/responses, added cancellation and bounded Fetch timeout, and removed sensitive response/parser details from errors. JSON credential input is masked. |
| Monetary integrity | Rejects overflowing amounts/codes/identities, missing revenue debit flags or amounts, and mismatched exported R/K minor-unit totals. JSON prettification uses decimal parsing to avoid a double-precision round trip. |
| Format | Preserved separate R period rows and blank H invoice number. Enforced priced R text's 54-character limit, fixed seven-part accounting dimensions, and rejects malformed periods and unsupported retained characters. |
| Local tool | Passes the original checkpoint into Convert, prints the proposed next ID, skips invoice-file writes on no work, uses strict Latin-1 encoding, and guards configuration/output path collisions. |
| Build/release | Added offline tests, warnings-as-errors validation, PR/publishing separation, a release test gate, and a package readme. Local/test executables are not packable. |

## Verification performed

- Release solution build: zero warnings and zero errors with warnings treated as errors.
- Final regression suite: **113 passed**, zero failed/skipped, directly on isolated **.NET 8.0.30**, including result/checkpoint serialization round trips. The preceding 107-case suite also passed using the machine's .NET 10 runtime.
- Previously saved Momentum payload: 14 invoices converted, checkpoint 14, 121 fixed-width records, 21,752 Latin-1 bytes. Record widths, blank H invoice number, encoding round trip, and empty replay using the returned checkpoint passed. No live API request or invoice delivery was made for this check.
- Production-library dependency advisory check, including transitive packages: no known vulnerable packages reported by the configured NuGet.org advisory source at audit time. This is not a guarantee against undisclosed vulnerabilities.
- NuGet package and source package inspected: expected task DLL/XML, root metadata listing two tasks, readme, and expected source files; no credentials, customer dumps, or local/test executables.
- Both workflow YAML files parsed; local CLI help and configuration-overwrite protection checked. GitHub publishing itself was not executed.

## Deployment gates still requiring environment confirmation

1. Confirm Momentum's checkpoint scope, monotonic IDs, complete batching, paging, retention, and event semantics. The task executes one sync query and cannot infer whether the server omitted an earlier node.
2. Install/test both tasks in Frends, explicitly wire the original checkpoint into Convert, and persist its returned checkpoint only after successful file delivery. Test crash/retry reconciliation; this package cannot make delivery and checkpoint storage atomic.
3. Acceptance-test the configured Raindance importer with ordinary/credit/VAT invoices and representative customer/accounting data. Confirm all required S fields are supplied, omitted H fields receive appropriate defaults, and both printed rental periods and K accounting periods are correct.

The converter intentionally supports only the documented customer classes, VAT IDs, revenue-account convention, created changes, and invoice-wide period source. It does not validate customer identity checksums or reconcile VAT/rounding against the complete invoice `toPay`. See the [field mapping](raindance-format.md) and [operations runbook](operations.md) for these boundaries and recovery instructions.
