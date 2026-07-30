# K'UHUL Forward Pass Architecture

## Core Insight: Forward Pass = One Semantic Collapse

The Hugging Face Transformers execution model validates the K'UHUL architecture:

```
A forward pass is ONE evaluation. Nothing repeats.
```

This aligns exactly with the concept that **a fold is one lawful semantic execution**.

---

## Execution Model Mapping

| Hugging Face | NNC-K / K'UHUL | Purpose |
|--------------|----------------|---------|
| `Tokenizer` | Regex / Roslyn / KAST / ELIZA `@node` recognition | Input preparation |
| `input_ids` | Canonical semantic input after XCFE admission | Tokenized input |
| `attention_mask` | XCFE admission field / semantic attention | Which tokens matter |
| `forward()` | One complete `Pop → Wo → Yax → Sek → Ch'en → Xul` fold cycle | **Single semantic collapse** |
| `logits` | Candidate semantic state before collapse | Raw predictions |
| `generate()` | Repeated fold cycles coordinated by `@flux` | **Autoregressive loop** |
| `labels` (optional) | Ground-truth target passed into the fold | forward() auto-computes training **loss** |
| `loss` | Training signal (present only when `labels` given) | LoRA / weight-update objective |
| `hidden_states` / `attentions` | Intermediate fold states / attention weights | Optional forward outputs (`output_hidden_states=True`) |

---

## The Three Stages

### 1. Pre-Forward (Semantic Field Preparation)

```
Prompt
   ↓
Regex
Roslyn
ELIZA
ADAM12
   ↓
Trinity semantic field
   ↓
XCFE admission
   ↓
[Ready for forward pass]
```

**Purpose**: Prepare the semantic context around the model.

**Key insight**: Trinity **isn't replacing the transformer's forward pass**. It is **modifying the semantic state before and around each forward pass**.

### 2. Forward (Model Execution)

```
[Admitted input]
   ↓
Pop (Input embedding)
   ↓
Wo (Attention projection)
   ↓
Yax (RoPE + KV cache)
   ↓
Sek (Attention scores)
   ↓
Ch'en (MoE expert routing)
   ↓
Xul (Output projection + collapse)
   ↓
logits
```

**Purpose**: One complete fold cycle through the network.

**Key insight**: The model weights never change. Only the field changes.

### 3. Post-Forward (Evaluation & Lineage)

```
logits
   ↓
collapse (token selection)
   ↓
output
   ↓
CollapseProof
   ↓
CHEESE
   ↓
CheeseRecord
   ↓
@flux (execution lineage)
   ↓
[next forward pass or termination]
```

**Purpose**: Evaluate what happened, record lineage, decide next step.

**Key insight**: CHEESE should **not** participate inside the forward pass. It evaluates **after** collapse.

---

## Forward vs. Generate: Critical Distinction

### `forward()` - Single Pass

```python
# Hugging Face
output = model(input_ids, attention_mask=attention_mask)
logits = output.logits

# NNC-K equivalent
hidden = embeds[token_ids]
for layer in range(num_layers):
    hidden = layer_forward(hidden, layer, pos, pager, kv)
logits = embeds @ hidden
```

**Characteristics**:
- ✅ One evaluation
- ✅ Returns raw logits
- ✅ Used for training and single-step inference
- ✅ No gradient tracking during inference (`torch.no_grad()`)
- ✅ Memory efficient

### `generate()` - Autoregressive Loop

```python
# Hugging Face
output = model.generate(
    input_ids,
    max_new_tokens=100,
    do_sample=True,
    top_k=50,
    temperature=0.7
)

# NNC-K equivalent
for i in range(max_tokens):
    # Forward pass
    for layer in range(num_layers):
        hidden = layer_forward(hidden, layer, pos, pager, kv)
    logits = embeds @ hidden
    
    # Sample next token
    next_id = sample_next_token(logits, temperature)
    
    # Update for next iteration
    hidden = embeds[next_id]
    pos += 1
```

**Characteristics**:
- ✅ Multiple forward passes
- ✅ Incorporates decoding strategies (beam search, top-k, sampling)
- ✅ Maintains KV cache across iterations
- ✅ Coordinates with `@flux` for execution lineage

---

## Experiment C Validation

During C2, the experimental results proved:

```
Forward + external semantic field → improved alignment
```

The model weights never changed. Only the Trinity field changed.

This validates that **pre-forward semantic preparation** (Trinity) can improve output quality **without** neural weight updates.

---

## Model Interchangeability

The three-stage separation keeps the model node interchangeable:

```
Pre-forward          Forward              Post-forward
────────────         ────────────         ────────────
Trinity              GPT-2                CollapseProof
Regex                LFM2.5               CHEESE
Roslyn               Qwen                 @flux
ELIZA                Custom model         BossPromotion
XCFE
```

Swapping models is "boring"—the orchestration, judgment, and promotion authority remain with K'UHUL/XCFE, CHEESE, and BOSS.

---

## Implementation in NNC-K Server

### Current State (`nnc_k_server.py`)

```python
def layer_forward(x, layer, pos, pager, kv, verify_sidecars=False):
    # 1. Attention branch (Pop → Wo → Yax → Sek)
    attn_norm_w, _, _, _ = pager.layer_norm(layer)
    normed = rms_norm(x, attn_norm_w)
    
    wq, _, _ = pager.load(layer, "q")
    wk, _, _ = pager.load(layer, "k")
    wv, _, _ = pager.load(layer, "v")
    wo, _, _ = pager.load(layer, "o")
    
    q = (normed @ wq[0].T).reshape(1, n_heads, head_dim)
    k = (normed @ wk[0].T).reshape(1, n_kv_heads, head_dim)
    v = (normed @ wv[0].T).reshape(1, n_kv_heads, head_dim)
    
    q = apply_rope(q, pos)  # Yax
    k = apply_rope(k, pos)
    
    kv.update(layer, k, v)  # KV cache
    k_cached, v_cached = kv.get(layer)
    
    attn_out = attention(q, k_cached, v_cached)  # Sek
    attn_out = attn_out.reshape(1, h_size)
    attn_out = attn_out @ wo[0].T  # Ch'en (attention output)
    x = x + attn_out
    
    # 2. MoE FFN branch (Ch'en → Xul)
    ffn_norm_w, _, _, _ = pager.layer_norm(layer)
    normed = rms_norm(x, ffn_norm_w)
    
    # Router (expert selection)
    router_logits = np.array([hash(f"{layer}-{pos}-{i}") % 1000 
                              for i in range(num_experts)])
    router_probs = softmax(router_logits)
    top_experts = np.argsort(router_probs)[-top_k:].tolist()
    
    # Expert processing (placeholder → HF forward pass)
    ffn_out = normed * 0.1  # TODO: Replace with asx_gemm_hf_forward
    
    x = x + ffn_out  # Xul (output projection + residual)
    return x
```

### Future State (with HF Forward Pass)

```python
def layer_forward(x, layer, pos, pager, kv, use_moe_sidecar=True):
    # Pre-forward: Trinity semantic field already applied by XCFE
    
    # Forward: Attention (unchanged)
    # ... [same as above] ...
    
    # Forward: MoE with HF-style routing
    if use_moe_sidecar:
        # Write tokens to sidecar input
        token_path = write_tokens(temp_dir / f"layer_{layer}_tokens.bin", normed)
        
        # Call HF forward pass sidecar
        result = run_sidecar("asx_gemm_hf_forward.exe", [
            str(pager.model_dir / f"layer_{layer:02d}"),
            str(config["hidden_size"]),
            str(config["num_experts"]),
            str(config["top_k_experts"]),
            "--tokens", str(token_path),
            "--gpu" if args.use_gpu else ""
        ])
        
        # Read expert outputs
        ffn_out = read_output(result["output_path"])
    else:
        # Placeholder (current)
        ffn_out = normed * 0.1
    
    # Post-forward: Return to main loop (CHEESE evaluates after all layers)
    x = x + ffn_out
    return x
```

---

## Performance Optimization Tips

### From Hugging Face → NNC-K

| HF Tip | NNC-K Equivalent |
|--------|------------------|
| Use `model(**inputs)` not `model.forward()` | Call `layer_forward()` not inline math |
| Use `torch.no_grad()` during inference | Skip gradient tracking in numpy ops |
| Use `attention_mask` to skip padding | XCFE admission filters invalid tokens |
| Batch sequences together | Process multiple tokens per forward pass |
| Use KV cache across generate() iterations | `KVCache` class maintains state |

### Memory Management

```python
# During inference (no gradients)
with torch.no_grad():  # HF
    output = model(input_ids)

# NNC-K equivalent (numpy has no gradient tracking)
# But we can still optimize:
output = layer_forward(x, layer, pos, pager, kv)
del x  # Free memory explicitly
gc.collect()
```

---

## Execution Lineage (`@flux`)

The `@flux` directive tracks execution across multiple forward passes:

```
Forward(0) → Collapse → CheeseRecord → @flux.record(step=0)
     ↓
Forward(1) → Collapse → CheeseRecord → @flux.record(step=1)
     ↓
Forward(2) → Collapse → CheeseRecord → @flux.record(step=2)
     ↓
...
```

This creates a complete audit trail of the generation process, enabling:
- Debugging (which forward pass produced which token)
- Rollback (revert to earlier state)
- Analysis (expert usage patterns, attention patterns)
- Promotion decisions (BossPromotion based on complete lineage)

---

## Summary

| Concept | Meaning |
|---------|---------|
| **Forward pass** | One semantic collapse (Pop→Wo→Yax→Sek→Ch'en→Xul) |
| **Generate** | Repeated forward passes coordinated by `@flux` |
| **Trinity** | Pre-forward semantic field preparation |
| **CHEESE** | Post-forward evaluation and proof |
| **`@flux`** | Execution lineage across multiple forward passes |
| **Model** | Interchangeable node (GPT, LFM, Qwen, custom) |

This architecture keeps concerns separated:
- **Pre-forward**: Semantic preparation (Trinity, XCFE)
- **Forward**: Model execution (interchangeable)
- **Post-forward**: Evaluation and lineage (CHEESE, `@flux`, BOSS)

---

**Status**: Architecture validated by HF execution model
**Last Updated**: 2026-07-28
**Reference**: Hugging Face Transformers Documentation [1-13]
