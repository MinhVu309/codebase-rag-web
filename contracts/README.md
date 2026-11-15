# contracts/

`openapi.yaml` is the **pinned copy** of the Web <-> RAG contract. It is owned by the RAG team; this repo only
mirrors it. **Do not edit it here.** If the contract has to change, tell Giang first and update Notion page
"03 — Quy ước & kiến trúc chung" before any code change.

| | |
|---|---|
| Source | https://github.com/CALLMEKANEKI/codebase-rag-service (public), path `contracts/openapi.yaml` |
| Commit | `d6e0038c4ec10822dae2047634a5c7102d8e432f` |
| Version (`info.version`) | `0.2.0-draft` |
| sha256 | `113faca43379d4167577f48b39b78276350999553d728120562588ce6478baa8` |

Verify the pin:

```bash
shasum -a 256 contracts/openapi.yaml    # must print the sha256 above
```

## Updating the pin

1. Agree the change with Giang and make sure Notion page 03 is updated first.
2. Copy the new file over `contracts/openapi.yaml` (byte for byte, no local edits).
3. Update the three rows above (commit, version, sha256) and the constants `PinnedSha256` / `PinnedCommit` in
   `backend/tests/Api.Tests/Rag/ContractExampleTests.cs`; also the sha256 line in `BACKEND_PLAN.md` section 0.
4. Run `dotnet test`. `ContractExampleTests` holds copies of the yaml examples; if the contract changed, update those
   samples and `HttpRagClient` to match.

## Using it

- **Mock (Prism):** `docker compose --profile mock up mock-rag` serves this file on http://localhost:4010 (see the
  root README, "Mock RAG").
- **Tests:** `ContractExampleTests` feeds the yaml's examples through `HttpRagClient`.
