# contracts/

`openapi.yaml` is the **pinned copy** of the Web <-> RAG contract. It is owned by the RAG team; this repo only
mirrors it. **Do not edit it here.** If the contract has to change, tell Giang first and update Notion page
"03 — Quy ước & kiến trúc chung" before any code change.

| | |
|---|---|
| Source | https://github.com/CALLMEKANEKI/codebase-rag-service (public), path `contracts/openapi.yaml` |
| Commit | `195e2a967509a2a2b6682604522cd071b7151ca1` (`195e2a9`), branch `work/wp6-retrieval-reranking`; the yaml itself last changed in `beb879c` |
| Version (`info.version`) | `0.3.0` |
| sha256 | `5f397fa87fb1fe2444f17d6fb4d7f033c2b1584a6ff7fed44d6e344daaadc606` |
| Status | **Pinned from an unmerged branch** (Contract v2 / D40, approved 25/11): pin again when RAG merges it into `main`. |

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
- **Tests:** `ContractExampleTests` feeds the yaml's examples through `HttpRagClient`; `MultiQueryContractTests` does
  the same for `POST /v1/query/multi` (200 partial + fallback, 503 `ALL_INDEXES_UNAVAILABLE`, error responses).
