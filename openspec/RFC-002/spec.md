# Specification: Multi-User Isolation

This document defines the requirements and test scenarios for isolating personal data between users while maintaining shared team data.

## 1. Requirements

- **REQ-1**: The server must identify the user via the `X-Engram-User` HTTP header.
- **REQ-2**: If the header is missing, the server must default the identity to `global`.
- **REQ-3**: Observations and prompts with `scope: personal` must be isolated to the requesting user.
- **REQ-4**: Observations and prompts with `scope: team` must be shared across all users in the same project.
- **REQ-5**: Search results and context generation must respect user isolation for personal data.

## 2. Scenarios

### Scenario 1: Personal Observation Isolation
**Given** two users: `user-alpha` and `user-beta`
**And** both are working on project `engram-dotnet`
**When** `user-alpha` saves an observation with `scope: personal` titled "My secret maña"
**Then** `user-beta` should NOT see "My secret maña" when requesting recent personal observations for `engram-dotnet`
**But** `user-alpha` SHOULD see "My secret maña" when requesting their own recent personal observations.

### Scenario 2: Shared Team Observations
**Given** two users: `user-alpha` and `user-beta`
**And** both are working on project `engram-dotnet`
**When** `user-alpha` saves an observation with `scope: team` titled "Team architecture decision"
**Then** both `user-alpha` AND `user-beta` SHOULD see "Team architecture decision" when requesting recent team observations.

### Scenario 3: Legacy Compatibility (Global User)
**Given** a request without the `X-Engram-User` header
**When** saving a personal observation
**Then** it should be stored under the `personal:global` scope
**And** any other request without the header should be able to see it.

### Scenario 4: Context Generation Isolation
**Given** `user-alpha` has a personal observation "Alpha preference"
**And** `user-beta` has a personal observation "Beta preference"
**When** `user-alpha` requests the context for the project
**Then** the context should contain "Alpha preference"
**And** it should NOT contain "Beta preference".
