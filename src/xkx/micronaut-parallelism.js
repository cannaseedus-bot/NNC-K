// micronaut-parallelism.js — Micronaut Parallelism: Batches, Threads, Processes, Network
//
// Pillar 3 of the complete micronaut execution stack:
//   Batch      sequential / parallel / staged / adaptive
//   Thread     fixed pool / dynamic pool / work-stealing + mutex/semaphore/barrier
//   Process    fork / container / sandbox + IPC (message queue / shared mem / socket pair)
//   Network    client-server / p2p / pubsub / service-mesh (discovery + load-balancer)
//
// K'UHUL physics connection:
//   BATCH.parallel  = Sek phase, concurrency governed by COMPUTE_FOLD pressure
//   BATCH.adaptive  = KuhulPhysicsSolver adjusting batch_size by loss/load
//   THREAD.mutex    = gravity constraint (one thread at a time)
//   THREAD.semaphore = rate limiting = TOOL_FOLD 0.8 pressure
//   PROCESS.sandbox  = OPCODE_FOLD 0.9 = tightest gravity
//   NETWORK.pubsub   = antigravity observe channels (like [dbg] telemetry)
//
// FLUX IR connection:
//   BATCH.parallel = XCFERuntime.parallel(tasks)
//   THREAD.mutex   = FluxRuntime single-threaded action queue (ordering guarantee)
//   PROCESS.fork   = spawn separate FLUX runtime instance
//   NETWORK.server = dispatch endpoint for external FLUX actions

// ─── BATCH ────────────────────────────────────────────────────────────────────

export class Batch {
  constructor(items, operation, opts = {}) {
    this.items     = [...items];
    this.operation = operation;
    this.opts      = { type:'parallel', concurrency:10, errorHandling:'continue', ...opts };
  }

  add(items)         { this.items.push(...items); return this; }
  filter(pred)       { this.items = this.items.filter(pred); return this; }
  map(fn)            { this.items = this.items.map(fn); return this; }

  async run() {
    const strategy = BatchStrategies[this.opts.type] ?? BatchStrategies.parallel;
    return strategy(this.items, this.operation, this.opts);
  }
}

export const BatchStrategies = Object.freeze({

  sequential: async (items, op, opts) => {
    const results = [];
    for (let i = 0; i < items.length; i++) {
      try {
        results.push({ ok:true, result: await op(items[i], { index:i, total:items.length }), index:i });
      } catch(e) {
        results.push({ ok:false, error: e.message, index:i });
        if (opts.errorHandling === 'stop') break;
      }
    }
    return _batchResult(items, results, opts);
  },

  parallel: async (items, op, opts) => {
    const concurrency = opts.concurrency ?? 10;
    const results = [];
    for (let i = 0; i < items.length; i += concurrency) {
      const chunk = items.slice(i, i + concurrency);
      const settled = await Promise.allSettled(chunk.map((item,j) => op(item, { index:i+j })));
      results.push(...settled.map((s,j) =>
        s.status === 'fulfilled'
          ? { ok:true, result:s.value, index:i+j }
          : { ok:false, error:s.reason?.message, index:i+j }
      ));
    }
    return _batchResult(items, results, opts);
  },

  staged: async (items, stages, opts) => {
    let current = items;
    for (const stage of (Array.isArray(stages) ? stages : [stages])) {
      const r = await BatchStrategies.parallel(current, stage.operation ?? stage, { concurrency: stage.concurrency ?? opts.concurrency });
      current = r.results.filter(x => x.ok).map(x => x.result);
      if (stage.aggregate) current = [stage.aggregate(current)];
    }
    return { ok:true, results: current, count: current.length };
  },

  adaptive: async (items, op, opts) => {
    let batchSz = opts.batchSize ?? 50;
    const target = opts.targetMs ?? 100;
    const maxSz  = opts.maxBatchSize ?? 1000;
    const results = [];
    for (let i = 0; i < items.length; i += batchSz) {
      const chunk = items.slice(i, i + batchSz);
      const t0 = Date.now();
      const r = await BatchStrategies.parallel(chunk, op, { concurrency: batchSz });
      const dur = Date.now() - t0;
      if (dur < target)        batchSz = Math.min(maxSz, batchSz + Math.ceil(batchSz*0.1));
      else if (dur > target*2) batchSz = Math.max(1,   batchSz - Math.ceil(batchSz*0.1));
      results.push(...r.results);
    }
    return _batchResult(items, results, opts);
  },
});

function _batchResult(items, results, opts) {
  let out = results;
  if (opts.aggregation === 'concat') out = results.flatMap(r => Array.isArray(r.result) ? r.result : [r.result]);
  else if (opts.aggregation === 'merge') out = Object.assign({}, ...results.filter(r=>r.ok).map(r=>r.result));
  const ok = results.filter(r=>r.ok).length, fail = results.filter(r=>!r.ok).length;
  return { ok: fail===0, results: out, raw: results, metrics: { total:items.length, ok, fail } };
}

// ─── THREAD primitives (JS equivalents) ──────────────────────────────────────

export class Mutex {
  constructor() { this._locked = false; this._q = []; }

  lock() {
    if (!this._locked) { this._locked = true; return Promise.resolve(); }
    return new Promise(r => this._q.push(r));
  }

  unlock() {
    const next = this._q.shift();
    if (next) next();
    else this._locked = false;
  }

  async withLock(fn) {
    await this.lock();
    try { return await fn(); }
    finally { this.unlock(); }
  }
}

export class Semaphore {
  constructor(max) { this._n = max; this._q = []; }

  acquire() {
    if (this._n > 0) { this._n--; return Promise.resolve(); }
    return new Promise(r => this._q.push(r));
  }

  release() {
    this._n++;
    const next = this._q.shift();
    if (next) { this._n--; next(); }
  }

  async withPermit(fn) {
    await this.acquire();
    try { return await fn(); }
    finally { this.release(); }
  }
}

export class Barrier {
  constructor(count) { this._count = count; this._waiting = 0; this._q = []; }

  async wait() {
    this._waiting++;
    if (this._waiting === this._count) {
      this._waiting = 0;
      this._q.forEach(r => r()); this._q = [];
    } else {
      await new Promise(r => this._q.push(r));
    }
  }
}

// ─── ThreadPool (JS Promise-based) ───────────────────────────────────────────

export class ThreadPool {
  constructor(size, opts = {}) {
    this._size  = size;
    this._queue = [];
    this._active = 0;
    this._type  = opts.type ?? 'fixed';
  }

  async submit(task) {
    return new Promise((resolve, reject) => {
      this._queue.push({ task, resolve, reject });
      this._drain();
    });
  }

  async _drain() {
    while (this._active < this._size && this._queue.length > 0) {
      const { task, resolve, reject } = this._queue.shift();
      this._active++;
      task().then(resolve, reject).finally(() => { this._active--; this._drain(); });
    }
  }

  async batch(tasks) {
    return Promise.all(tasks.map(t => this.submit(t)));
  }

  get pending() { return this._queue.length; }
  get active()  { return this._active; }
}

// ─── MessageQueue (process IPC) ───────────────────────────────────────────────

export class MessageQueue {
  constructor() { this._q = []; this._listeners = []; }

  send(msg) {
    this._q.push(msg);
    const l = this._listeners.shift();
    if (l) l(this._q.shift());
  }

  receive() {
    if (this._q.length > 0) return Promise.resolve(this._q.shift());
    return new Promise(r => this._listeners.push(r));
  }

  get size() { return this._q.length; }
}

// ─── Sandbox (JS vm-style) ────────────────────────────────────────────────────

export class Sandbox {
  constructor(opts = {}) {
    this._ctx     = { ...opts.globals };
    this._timeout = opts.timeoutMs ?? 5000;
    // §46 authority gate: compiling a STRING into code via new Function() is NOT isolated
    // (it executes in the global scope). Disabled by default so a data-only input can NEVER
    // become executable code. Callers pass a function, or must explicitly opt in with
    // { allowCodeStrings: true } -- a deliberate, non-authority capability grant.
    this._allowCodeStrings = opts.allowCodeStrings === true;
  }

  setGlobal(name, val) { this._ctx[name] = val; }
  getGlobal(name)      { return this._ctx[name]; }

  async run(codeOrFn) {
    // Browser/Node safe: wrap in async timeout
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error('sandbox timeout')), this._timeout);
      try {
        let fn;
        if (typeof codeOrFn === 'function') {
          fn = codeOrFn;                                  // trusted callback: always allowed
        } else if (this._allowCodeStrings && typeof codeOrFn === 'string') {
          fn = new Function(...Object.keys(this._ctx), `return (async()=>{ ${codeOrFn} })()`);
        } else {
          // §46: data-only string cannot become code unless explicitly authorized.
          clearTimeout(timer);
          resolve({ ok:false, error:'sandbox: string code execution is disabled (data-only inputs cannot become code); pass a function, or construct with { allowCodeStrings: true }' });
          return;
        }
        Promise.resolve(fn(...Object.values(this._ctx))).then(r => { clearTimeout(timer); resolve({ ok:true, result:r }); }, e => { clearTimeout(timer); resolve({ ok:false, error:e.message }); });
      } catch(e) { clearTimeout(timer); resolve({ ok:false, error:e.message }); }
    });
  }
}

// ─── Network stubs ────────────────────────────────────────────────────────────
//
// Full implementations require platform APIs (fetch, WebSocket, WebRTC).
// These stubs provide the interface contract; wire real backends as needed.

export const Network = Object.freeze({

  /** Client-server: create an in-process event hub (browser/Node agnostic). */
  createHub() {
    const handlers = new Map();
    return {
      on(path, method, fn) {
        handlers.set(`${method}:${path}`, fn);
      },
      async call(path, method = 'GET', body = null) {
        const key = `${method}:${path}`;
        const fn  = handlers.get(key);
        if (!fn) throw new Error(`No handler: ${method} ${path}`);
        return fn(body);
      },
    };
  },

  /** PubSub: in-process event bus. */
  createPubSub() {
    const subs = new Map();
    return {
      publish(channel, msg) {
        (subs.get(channel) ?? []).forEach(h => h(msg));
      },
      subscribe(channel, handler) {
        if (!subs.has(channel)) subs.set(channel, []);
        subs.get(channel).push(handler);
        return () => { const arr = subs.get(channel); if(arr) { const i=arr.indexOf(handler); if(i>=0) arr.splice(i,1); } };
      },
    };
  },

  /** Load balancer (round-robin / least-conn / weighted). */
  loadBalancer(backends, strategy = 'round_robin') {
    let idx = 0;
    const conns = new Map(backends.map(b => [b, 0]));
    const strategies = {
      round_robin:     () => backends[idx++ % backends.length],
      least_connections: () => backends.reduce((a,b) => (conns.get(a)??0) < (conns.get(b)??0) ? a : b),
      random:          () => backends[Math.floor(Math.random()*backends.length)],
      weighted:        () => {
        const total = backends.reduce((s,b) => s+(b.weight??1), 0);
        let r = Math.random()*total;
        for (const b of backends) { r -= (b.weight??1); if(r<=0) return b; }
        return backends[0];
      },
    };
    return {
      pick: () => strategies[strategy]?.() ?? backends[0],
      recordRequest(backend, ok) { conns.set(backend, (conns.get(backend)??0) + (ok?0:1)); },
      health: () => backends.map(b => ({ backend:b, connections:conns.get(b)??0 })),
    };
  },
});

// ─── ParallelOrchestrator ─────────────────────────────────────────────────────

export class ParallelOrchestrator {
  constructor() {
    this._pools   = new Map();
    this._pubsub  = Network.createPubSub();
    this._hub     = Network.createHub();
    this._breakers = new Map();
  }

  pool(name, size, opts = {}) {
    if (!this._pools.has(name)) this._pools.set(name, new ThreadPool(size, opts));
    return this._pools.get(name);
  }

  batch(items, op, opts) { return new Batch(items, op, opts); }
  sandbox(opts)          { return new Sandbox(opts); }
  mutex()                { return new Mutex(); }
  semaphore(max)         { return new Semaphore(max); }
  barrier(n)             { return new Barrier(n); }
  queue()                { return new MessageQueue(); }

  pubsub() { return this._pubsub; }
  hub()    { return this._hub; }

  loadBalancer(backends, strategy) { return Network.loadBalancer(backends, strategy); }

  // Run tasks with automatic parallelism selection
  async run(tasks, opts = {}) {
    const { strategy = 'parallel', concurrency = 10 } = opts;
    const pool = this.pool('default', concurrency);
    return Promise.all(tasks.map(t => pool.submit(() => t())));
  }

  summary() {
    const pools = {};
    for (const [name, p] of this._pools) pools[name] = { active:p.active, pending:p.pending };
    return { pools };
  }
}
