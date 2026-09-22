# ADR-0011: Require administrator-provisioned policy before Gemini CLI execution

Status: Accepted

## Context

Gemini CLI 0.60.0 is not currently safe to enable as an implementation provider.
Its system settings are administrative configuration and require trusted
administrator ownership and permissions. A settings file created by DevalCopilot
inside user-owned attempt scratch is not an acceptable administrative policy.
No trusted `C:\ProgramData\gemini-cli` policy is provisioned on this host.

## Decision

Gemini is not currently selectable or claimable. DevalCopilot must never
manufacture Gemini administrative policy in user-owned scratch state, change its
ACLs to simulate administrator provisioning, or otherwise elevate the policy
file at runtime.

A future slice may enable Gemini only after it defines administrator-controlled
installation and policy provisioning, ACL validation, exact semantic policy
validation, readiness reporting, and a real no-model contract test. Gemini
authentication material must never be copied into scratch or persistence. No
automatic fallback or parallel execution is authorized.

## Consequences

The durable assignment foundation is independent of Gemini operational support.
Until the provisioning and validation boundary is implemented and reviewed,
the initial Implementer remains Claude Code only. Automated tests must remain
deterministic and must not invoke a real provider or model.
