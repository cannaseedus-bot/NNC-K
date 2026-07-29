# Chat Bubble Metadata Badge

Every assistant response in the WPF chat should display a metadata badge below the bubble. This keeps the model node visible and accountable without giving it authority.

## Observed confidence and persistent topic memory

The UI exposes refinement as a traceable sequence. In the captured topic, an
unresolved question starts at `0`, user clarification constrains the route to
`0.70`, further reasoning raises it to `0.90`, and a precise distinction
between organic compounds and life reaches `0.95`.

| Stage | Visible runtime state | Capture |
|---|---|---|
| Factory baseline | A new topic Micronaut is scaffolded at `conf:0.5` | [Open image](screenshots/micronaut-confidence-baseline.png) |
| Unresolved | No applicable brain; confidence `0` | [Open image](screenshots/micronaut-confidence-00.png) |
| Corrected | Relevant brain; confidence `0.70` | [Open image](screenshots/micronaut-confidence-07.png) |
| Constrained | Relevant brain; confidence `0.90`; register updated | [Open image](screenshots/micronaut-confidence-09.png) |
| Refined | Insightful brain; confidence `0.95`; register updated | [Open image](screenshots/micronaut-confidence-095.png) |

[![Micronaut confidence reaches 0.95 after topic refinement](screenshots/micronaut-confidence-095.png)](screenshots/micronaut-confidence-095.png)

The important result is durable recall. Admitted topic objects are written to
the persistent memory plane and mounted into XCFE's resident Micronaut index.
When the same or a semantically related topic is asked later—even after a
restart—the runtime can retrieve the learned constraints rather than begin
from the unresolved state. The later score is still recomputed for the new
prompt; persistence supplies prior evidence, not an unconditional confidence
guarantee.

To verify this behavior, close and relaunch the UI, ask the original topic in
different words, and inspect the Runtime tab and badge for retrieved
Micronauts, the selected brain, provenance, and the new confidence score.

## What the badge shows

```text
[ Tick#247 | lfm-2.5-1.2b | brain:insightful | fold:Sek | intent:science | conf:0.98 | micronauts | micronaut: organic-compound created | 14:32:07 ]
```

Fields:

| Field | Source |
|---|---|
| `Tick#N` | `$turnTick` |
| model name | `$script:ActiveModel` |
| `brain` | `$route.Brain` |
| `fold` | `$route.Fold` or resolved fold |
| `intent` | `$route.Intent` |
| `conf` | `$route.Confidence` rounded |
| sources | web / BOSS / micronauts / replay / mutation+ / CHEESE / local |
| micronaut actions | created / updated / merged names from `Save-Micronaut` / `MicronautManager` |
| timestamp | turn time |

## Integration into `micronaut-ui.ps1`

> **Status:** Implemented in `micronaut-ui.ps1` as of this revision. The standalone `NNCK-Runtime.psm1` module provides `Format-ChatMetadata` and `New-ChatMetadataBadge`. If another model removes the UI wiring, re-apply the four steps below.

### 1. Capture metadata during the turn

Inside the send handler, after `$turnTick` is assigned and before the model call, initialize a metadata hashtable:

```powershell
$script:TurnMeta = @{
    Tick = $turnTick
    Model = $script:ActiveModel
    Brain = $route.Brain
    Fold = if ($route -and $route.Fold) { $route.Fold } else { "Sek" }
    Intent = $route.Intent
    Confidence = $route.Confidence
    Sources = @{
        Web = $false
        Boss = $false
        Micronauts = ($relevantMicros.Count -gt 0)
        Replay = $false
        MutationPlus = $false
        Cheese = $false
        Local = ($relevantMicros.Count -eq 0)
    }
    MicronautActions = @()
    Timestamp = (Get-Date -Format 'HH:mm:ss')
}
```

Set flags as the turn proceeds:

```powershell
# BOSS advice used
if ($usedBoss) { $script:TurnMeta.Sources.Boss = $true }

# Web search / tool invocation
if ($toolName -eq 'web_search' -or $script:WebUsed) { $script:TurnMeta.Sources.Web = $true }

# Replay
if ($script:ReplayMode) { $script:TurnMeta.Sources.Replay = $true }

# Mutation+
if ($script:MutationPlusUsed) { $script:TurnMeta.Sources.MutationPlus = $true }

# CHEESE judged the collapse
if ($cheeseRecord) { $script:TurnMeta.Sources.Cheese = $true }
```

### 2. Track micronaut actions

When `Save-Micronaut` creates or updates a micronaut:

```powershell
$script:TurnMeta.MicronautActions += "micronaut: $id $action"
```

When `MicronautManager` merges or normalizes a micronaut:

```powershell
$script:TurnMeta.MicronautActions += "micronaut: $subject merged"
```

### 3. Render the badge below the assistant bubble

Replace the manual assistant bubble construction at the response site with a call that adds the badge:

```powershell
function Add-AI($t, $tick = $null, $meta = $null) {
    try {
        $window.Dispatcher.Invoke([action]{
            $outer = [Windows.Controls.StackPanel]::new()
            $outer.Margin = '0,4,0,0'
            $outer.HorizontalAlignment = 'Left'

            # response bubble
            $b = [System.Windows.Controls.Border]::new()
            $b.CornerRadius = '12'; $b.Padding = '14,10,14,10'; $b.MaxWidth = 720
            $b.Background = '#21262d'; $b.HorizontalAlignment = 'Left'
            $tx = [System.Windows.Controls.TextBox]::new()
            $tx.Text = $t; $tx.TextWrapping = 'Wrap'; $tx.FontSize = 13
            $tx.Foreground = '#e2e8f0'; $tx.IsReadOnly = $true
            $tx.Background = 'Transparent'; $tx.BorderThickness = '0'; $tx.Cursor = 'IBeam'
            $b.Child = $tx
            $outer.Children.Add($b)

            # metadata badge
            if ($meta) {
                $badge = New-ChatMetadataBadge `
                    -Tick $meta.Tick `
                    -Model $meta.Model `
                    -Confidence $meta.Confidence `
                    -Brain $meta.Brain `
                    -Fold $meta.Fold `
                    -Intent $meta.Intent `
                    -MicronautActions $meta.MicronautActions `
                    -Sources $meta.Sources `
                    -Timestamp $meta.Timestamp
                if ($badge -is [System.Windows.Controls.TextBlock]) {
                    $outer.Children.Add($badge)
                }
            }

            $feed.Children.Add($outer)
            $feedScroll.ScrollToBottom()
        }, "Normal")
    } catch { }
}
```

Call it from the response handler:

```powershell
Add-AI $txt $turnTick $script:TurnMeta
```

### 4. Persist metadata with the chat message

In `Save-Chat`, include the metadata in the saved message:

```powershell
$script:Conversation += @{
    role = "assistant"
    content = $txt
    tick = $turnTick
    meta = $script:TurnMeta
}
```

In `Load-ChatById`, restore it:

```powershell
elseif ($m.role -eq "assistant") {
    Add-AI $m.content $m.tick $m.meta
}
```

## Authority note

The badge makes the model node visible, but the model remains just a node. Confidence values are semantic-fabric scores, not model self-assessments. A `0.50` value means the knowledge is provisional and awaits CHEESE/BOSS processing.

## Worked example: organic life vs organic compounds

This conversation demonstrates how the badge records semantic refinement across turns.

### Turn 0 — Semantic page fault triggers research

```text
User: What tyo of natural organic compunds does Saturn have?
```

No existing micronaut matches. The runtime calls `Research-And-MintMicronaut`:

- Web search retrieves an abstract about Saturn's organic compounds.
- Runtime calls `micronaut_factory.exe create chemistry` (or derived domain) to scaffold the formal `.micronaut` package.
- A provisional JSON micronaut `saturn-organic-compounds` is minted at `0.50` for the NNC-K runtime.
- `RouteTurn` is re-run; intent is now `science`, brain `ResearchAssistant`.

Assistant badge:

```text
[ Tick#11 | lfm-2.5-1.2b | brain:ResearchAssistant | fold:Pop | intent:science | conf:0.50 | web | factory: chemistry scaffolded; micronaut: saturn-organic-compounds created (0.50) | 09:11:22 ]
```

Notes:

- `web` source because research used DuckDuckGo.
- `conf:0.50` because the micronaut is provisional.
- The model node generated the final response, but only after the semantic fabric supplied context.

### Turn 1 — User asks broadly about organic molecules in space

```text
User: tell me about organic molecules in space
```

Assistant badge:

```text
[ Tick#1 | lfm-2.5-1.2b | brain:ResearchAssistant | fold:Pop | intent:science | conf:0.45 | local | micronaut: organic-molecule created (0.50) | 09:12:34 ]
```

Notes:

- No existing micronaut matched, so `local` source.
- Runtime researched and minted `organic-molecule` at provisional confidence `0.50`.
- Overall route confidence is only `0.45` because the neighborhood was sparse.

### Turn 2 — User narrows to habitability

```text
User: does that mean life?
```

Assistant badge:

```text
[ Tick#2 | lfm-2.5-1.2b | brain:ResearchAssistant | fold:Wo | intent:science | conf:0.50 | micronauts | micronaut: organic-compound created (0.50); edge: organic-compound → life-present rejected | 09:13:02 ]
```

Notes:

- `organic-compound` micronaut created.
- CHEESE rejected the `organic-compound → life-present` edge.
- Confidence stays at `0.50` because the relationship is provisional/guarded.

### Turn 3 — User supplies the correction

```text
User: organic compound is the correct chemistry term. It does not mean biological or living.
```

Assistant badge:

```text
[ Tick#3 | lfm-2.5-1.2b | brain:ResearchAssistant | fold:Sek | intent:science | conf:0.98 | micronauts | micronaut: organic-compound updated; edge: organic-compound → carbon-chemistry accepted; edge: organic-compound → life-present rejected | 09:14:18 ]
```

Notes:

- `organic-compound` micronaut updated.
- CHEESE accepted `organic-compound → carbon-chemistry`.
- `life-present` edge remains rejected.
- Confidence jumps to `0.98` because the semantic graph is now well constrained.

### Turn 4 — Follow-up about natural organic life

```text
User: so natural "forming" organic life is spread all over the galaxy?
```

Assistant badge:

```text
[ Tick#4 | lfm-2.5-1.2b | brain:ResearchAssistant | fold:Sek | intent:science | conf:0.99 | micronauts | edge: organic-compound + environment + evidence → habitability-indicator guarded; CHEESE: guarded | 09:15:47 ]
```

Notes:

- A guarded edge is added: habitability indicator under conditions, not life detection.
- CHEESE marks it `guarded` (0.5 reward).
- Confidence is high because the graph is consistent, but the edge itself remains provisional.

### Summary progression

| Turn | Key micronaut action | CHEESE verdict | Route confidence |
|---|---|---|---|
| 0 | `saturn-organic-compounds` created @ 0.50 via research | none (provisional) | 0.50 |
| 1 | `organic-molecule` created @ 0.50 | none | 0.45 |
| 2 | `organic-compound` created @ 0.50; `→ life-present` rejected | rejected | 0.50 |
| 3 | `organic-compound` updated; `→ carbon-chemistry` accepted | accepted | 0.98 |
| 4 | guarded `→ habitability-indicator` | guarded | 0.99 |

The badge makes the refinement visible: the model did not "become more confident" on its own. The semantic graph became better constrained, and the confidence score reflects the graph state, not model self-assessment.
