# CHEESE Wiring Plan — Production C (post-Xul judgment)

**Status:** 📋 Planned (interfaces verified against source; not yet wired)
**Scope of THIS plan:** wire the existing, orphaned `Runtime/` CHEESE chain into the live turn so
that collapse produces a judged, persisted `CheeseRecord`. **In scope:** `CollapseProof → CheeseJudge
→ CheeseRecord → ProvenanceStore`. **Out of scope (deferred, below the frozen promotion line):**
`BossPromotion` (D), `FluxFieldLearner` consuming CheeseRecord history, and any Trinity-field update.

Frozen equation this honors: `@node`=proposal / `@fold`=lawful execution / `@flux`=lineage /
**CHEESE=judgment** / BOSS=promotion. Authority: model proposes, K'UHUL collapses, **CHEESE judges
(cannot collapse or promote)**, BOSS promotes. Production signal = **edge-level `CheeseRecord`
history**, not `FluxTrace.Success`.

---

## 1. Verified interfaces (from source, not the doc)

- `CollapseProof` — `{ SessionId, Tick, Intent, Brain, Confidence, SelectedEdges:List<CollapsedEdge>,
  RejectedNodeIds, ContributionHashes, FoldTraceHash, CollapsedAt }`; `ComputeHash()` = SHA256.
  `CollapsedEdge = { Source, Relation, Target, Guard:EdgeGuard(None|Accepted|Rejected|Guarded), Weight }`.
- `CheeseJudge.Judge(CollapseProof, IReadOnlyList<NodeContribution>? ) → CheeseRecord`. Per edge:
  `Classify` (uses `edge.Guard`; else heuristic: overreach targets→Rejected, "may/possible/indicator"
  relations→Guarded, else Accepted) → verdict + `Reward` (Accepted 1.0 / Guarded 0.5 / Rejected 0.0)
  → `record.Seal()`. **Contributions arg is currently unused by `Reward`** (safe to pass null initially).
  Works WITHOUT the contract file (`ValidateContract` is separate/optional).
- `CheeseRecord` — `{ SessionId, Tick, CollapseProofHash, Invariants, Judgments:List<CheeseJudgment>,
  JudgedAt, ProvenanceHash(after Seal) }`.
- `ProvenanceStore(root)` — `Save(CollapseProof)→collapse/{hash}.json`, `Save(CheeseRecord)→
  cheese/{ProvenanceHash}.json`, append-only; `LoadCheeseHistory(source,relation,target)→ordered
  CheeseRecords` (the reinforcement history).

---

## 2. THE key data-mapping decision

CHEESE judges **semantic edges**, so `CollapseProof.SelectedEdges` must be the meaningful
relationships that survived collapse — the **`NodeContribution.Relations`** (A→[rel]→B) of the
contributions that were actually selected — **NOT** the fold-glyph `ResultTransitions`
(Pop→Wo→…), which are `@flux` *execution lineage*, not semantic claims.

```
CollapseProof.SelectedEdges     <- Relations of the SELECTED $microContributions (semantic edges)
CollapseProof.RejectedNodeIds   <- relevantMicros considered but NOT contributing this turn
CollapseProof.ContributionHashes<- NodeContribution.ProvenanceHash of the selected contributions
CollapseProof.FoldTraceHash     <- SHA256(route.FoldTrace)          (links proof <-> @flux lineage)
CollapseProof.{SessionId,Tick,Intent,Brain,Confidence} <- FluxStore.SessionId, $turnTick, $route.*
```
`EdgeGuard` initial policy (decision, see §6): default `None` → let `CheeseJudge.Classify` heuristics
decide; OR set from the contributing micronaut's confidence/capability. Start with `None` (heuristic).

---

## 3. Where it wires (turn loop)

`micronaut-ui.ps1` send handler: RouteTurn (2872) → collapse/trace built (2908) → micronauts run,
`$microContributions` complete (2971) → **@flux finalize (Contributions)**. CHEESE runs **right after
that finalize** — post-collapse, all inputs present, before Save-Chat persists the trace:

```
2971  $microContributions complete
 →    @flux finalize: ExecutionTraces[$turnTick].Contributions = $microContributions   (already wired)
 →    CHEESE:  build CollapseProof -> CheeseJudge.Judge -> ProvenanceStore.Save(proof), Save(record)
 →    surface: ExecutionTraces[$turnTick].Cheese = verdict summary ; TurnMeta.Sources.Cheese = $true
```

## 4. Recommended shape: a C# orchestrator, PS calls it once

Keep the chain in C# (testable, "runtime owns judgment"); PS just invokes it. Add
`Runtime/CheesePipeline.cs`:
```
public sealed class CheesePipeline {
  CheesePipeline(string provenanceRoot);
  CheeseRecord JudgeTurn(string sessionId, long tick, string intent, string brain, double confidence,
                         IEnumerable<CollapsedEdge> selectedEdges, IEnumerable<string> rejectedNodeIds,
                         IEnumerable<string> contributionHashes, string foldTraceHash);
  // builds CollapseProof, calls CheeseJudge.Judge, Save(proof)+Save(record), returns record
}
```
PS builds the `CollapsedEdge[]` from `$microContributions` relations and calls `JudgeTurn(...)`.
(Alternative: PS drives `[…Runtime.CollapseProof]::new()` + `CheeseJudge` + `ProvenanceStore` directly
— more `Add-Type` glue, no new C# file. The orchestrator is cleaner + unit-testable.)

## 5. @flux integration (truthful)

Add a tolerant `Cheese` field to `FluxTrace` (default null) carrying the verdict summary
`{ recordHash, accepted, guarded, rejected }`. The badge `Cheese=$false` (line 2889) becomes `$true`
only when a CheeseRecord was actually emitted — never inferred from success/confidence. This keeps
the two-stage trace: CHEESE fields populate *after* the collapse they judge.

---

## 6. Decisions needed before implementation

1. **EdgeGuard source** — heuristic (`None` → CheeseJudge policy) vs. micronaut-confidence-derived.
   Recommend heuristic first (matches existing Classify), tune later via `cheese.contract.xjson`.
2. **Classify policy** — currently hardcoded domain terms ("life-present", "may/possible"). Make it
   contract-driven (`contracts/cheese.contract.xjson`) or accept the heuristic as v0. Recommend v0
   heuristic; contract is a follow-up.
3. **Contributions → NodeContribution** — pass `null` initially (Reward ignores it), or convert the
   PS `$microContributions` PSCustomObjects to C# `NodeContribution`. Recommend `null` for v0.
4. **Orchestrator vs. direct PS glue** (§4) — recommend the `CheesePipeline.cs` orchestrator.

## 7. Verification

- **Unit (C#):** `CheesePipeline.JudgeTurn` with synthetic edges → assert verdicts (Accepted/Guarded/
  Rejected) + rewards; assert `ProvenanceStore` wrote `collapse/` and `cheese/` artifacts; assert
  `LoadCheeseHistory` returns the edge. (Add to tests under the frozen source roots.)
- **Turn (PS):** one real turn → a `CollapseProof` + `CheeseRecord` appear under `.learning/provenance/`,
  badge `Cheese=$true`, and `FluxTrace.Cheese` populated. `Cheese=$false` when no edges selected.
- **Authority (negative):** CHEESE never writes to Xul/route; `ProvenanceStore` is append-only;
  `BossPromotion.Save` still refuses without CHEESE history (unchanged).

## 8. Honest scope

Delivers the *judgment + provenance* layer only. It does NOT: promote (BOSS/D), update the Trinity
field (that's the FluxFieldLearner reading `LoadCheeseHistory` — separate), or replace the heuristic
Classify with a real contract. Those stay above/after this, per the frozen boundary. Production-C's
"real Success signal" is the edge-level `CheeseRecord` history this produces — replacing the
`FluxTrace.Success` boolean the C1/C2 experiments proved insufficient.
