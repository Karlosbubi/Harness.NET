# Acceptance evidence

Acceptance records describe the exact behavior, environment, and verification that
closed a slice. Durable screenshots and text evidence may be committed in this
directory only when they are bounded, useful to review, and deliberately checked for
source, prompt, conversation, path, credential, log, and other sensitive content.

Output below the ignored `artifacts/` directory is machine-local reproduction data.
An acceptance record may document its expected path and cleanup command, but must
label it machine-local and must not treat its presence as durable evidence available
to another clone. Historical output that was not retained is described, not
reconstructed or silently promoted.

The policy is fixed by
[ADR 027](../decisions/027-contributor-verification-and-dependency-governance.md).
CI verifies local Markdown targets and rejects ambiguous `Artifact:` labels in these
records.
