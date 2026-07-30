# FluxFieldLearner Plan — inline semantic-field learning from CHEESE

**Status:** 📋 Planned (grounded in shipped interfaces; not yet built)
**Depends on:** Production C v0 (CHEESE wired, `483e87b`) — the source of `CheeseRecord` history.
**In scope:** read CHEESE verdicts → update an **inline** edge-weight field via the `@flux` rule.
**Out of scope (above/below the frozen line):** BOSS/`BossPromotion` (D); feeding the learned field
back to bias routing/collapse ("field guides the tokens" loop); any neural-model update.

This is the **non-proxy version of C2**. C2 proved value-discriminative evidence (PMI *sign* proxy)
improves the field with the model frozen (+1.03/+1.34 nats). CHEESE verdicts are the **real** value
signal (Accepted/Guarded/Rejected → 1.0/0.5/0.0), so this closes experiment → production.

---

## 1. The three-tier field (grounded)

There is NO existing runtime edge-weight field. Introduce one as the **inline** tier:

```
PRIOR      bigrams.json / trigrams.json  (KUHUL_pi, AUTHORED, read-only)   <- bootstrap, never mutated
INLINE     SemanticFieldStore (NEW)      (.learning/field/field.json)      <- learned from CHEESE
PERSISTENT SCXQ2 / BossPromotion (D)                                       <- promoted, deferred
```
Node-level `Scores` (GasNode/MicronautNetworkNode) and MicronautStore Quality are separate
(node/capability level) and are NOT the edge field — leave them alone.

## 2. Components (new)

**`Runtime/SemanticFieldStore.cs`** — persistent edge→weight map.
```
key   = "{Source}|{Relation}|{Target}"   (same identity as CollapsedEdge)
value = double in [0,1]
double GetWeight(edge)            // default 0.5 (provisional) if unseen
void   Reinforce(edge, target, lr)// w += lr*(target - w), clamp [0,1]
Load()/Save(.learning/field/field.json)   // JSON, append-safe (last-writer per key)
```

**`Runtime/FluxFieldLearner.cs`** — CHEESE → field.
```
LearnFromRecord(CheeseRecord record, double lr = 0.1):
  foreach judgment in record.Judgments:
     store.Reinforce(judgment.Edge, target = judgment.Reward, lr)   // Accepted 1.0 / Guarded 0.5 / Rejected 0.0
  store.Save()
// or LearnFromHistory(store, provenance, source,rel,target): replay LoadCheeseHistory for one edge
```

## 3. The update rule (= @flux inline, real signal)

`w(t+1) = w(t) + lr * (reward - w(t))`, clamped [0,1]. New edge starts at **0.5** (provisional,
per SemanticKernel.md lifecycle). Repeated **Accepted** drives `0.50 → 0.55 → 0.595 → …→ ~1.0`
(the `.70→.73→.77` trajectory, now CHEESE-driven not co-occurrence); **Rejected** drives toward 0;
**Guarded** pulls toward 0.5. This is exactly the flux() rule the Python harness proved, with
`reward` = the CHEESE verdict instead of the PMI proxy.

## 4. Where it wires

Right after `CheesePipeline.JudgeTurn` returns a record (per-tick), in the same guarded block:
```
$rec = CheesePipeline.JudgeTurn(...)          # already wired
if ($rec) { $script:FieldLearner.LearnFromRecord($rec) }   # NEW: one call
```
Cleanest as a C# method the PS calls once (like CheesePipeline). Per-tick, not batch, so each
verdict nudges the field immediately.

## 5. Authority invariants (frozen)

- Learner updates the **inline field ONLY**. It does NOT touch the model (frozen), the authored
  prior (bigrams.json, read-only), or promotion (BOSS).
- It consumes CHEESE output; it never judges or collapses. Order stays:
  `collapse → CollapseProof → CHEESE → CheeseRecord → FluxFieldLearner(inline field) → [BOSS, deferred]`.
- Field weight near 1.0 marks an edge **BOSS-promotable** (D reads this later; not promotion itself).

## 6. Verification

- **Unit (C#):** feed synthetic CheeseRecords → assert Accepted edge weight rises, Rejected falls,
  Guarded → 0.5; assert the same edge over N Accepted records approaches 1.0 monotonically; assert
  persistence round-trips.
- **Trajectory:** replay `LoadCheeseHistory(edge)` → weight follows `0.50 → … → ~1.0` (the user's
  `.70→.73→.77`), now from real judgments.
- **Bridge (optional, honest):** export the inline field to the Python evaluator to reconfirm the
  C2-style alignment gain with a REAL signal (the field is C#, the eval model is Python — same
  bridge as ForwardPass.ps1). Not required for v0.

## 7. Honest scope & decisions

v0 delivers: SemanticFieldStore + FluxFieldLearner + per-tick wiring + tests. It does NOT yet **use**
the learned field to bias routing/collapse (that "field guides" feedback is the next step), does NOT
promote (BOSS/D), and does NOT bridge to the neural model.

**Decisions before code:**
1. **Init value** — new edge = 0.5 provisional (lifecycle-faithful) vs. seed from bigram prior. *Rec: 0.5.*
2. **Timing** — per-tick after JudgeTurn vs. batch. *Rec: per-tick.*
3. **Learning rate** — fixed `lr=0.1`, target=CheeseJudgment.Reward, clamp [0,1]. *Rec: yes.*
4. **Guidance feedback** — v0 learn+store+queryable only; field→routing/collapse bias deferred. *Rec: defer.*
