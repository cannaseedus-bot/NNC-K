/**
 * K'UHUL Browser Runtime — loads, parses, and executes .kuhul programs
 * in the browser via WebBrowser/Electron/webview.
 *
 * Bridge interface exposed to the host:
 *   KuhulRuntime.load(url)       — fetch and compile a .kuhul program
 *   KuhulRuntime.run(input)      — execute the loaded program with input
 *   KuhulRuntime.getState()      — current fold phase and node state
 *
 * Called by the host (PowerShell / C#):
 *   window.external.KuhulEvent(phase, node, result)
 */
(function () {
    'use strict';

    const FOLDS = ['Pop', 'Wo', 'Yax', 'Sek', "Ch'en", 'Xul'];
    const FOLD_ORDER = { Pop: 0, Wo: 1, Yax: 2, Sek: 3, "Ch'en": 4, Xul: 5 };

    let _program = null;
    let _state = { fold: 0, tick: 0, memory: {}, context: {} };
    let _foldData = {};

    /**
     * Load a .kuhul JSON program from a URL or inline object.
     */
    function load(source) {
        if (typeof source === 'string') {
            return fetch(source)
                .then(r => r.json())
                .then(prog => { _program = prog; init(); return prog; });
        }
        _program = source;
        init();
        return Promise.resolve(_program);
    }

    function init() {
        if (!_program || !_program.folds) return;
        _state.fold = 0;
        _state.tick = 0;
        _foldData = {};
        _program.folds.forEach((f, i) => {
            _foldData[f.name] = { nodes: f.nodes || [], index: i };
        });
        emit('init', _program.meta || {});
    }

    /**
     * Execute one fold step. Returns the current fold's output.
     */
    function step(input) {
        if (!_program) return { error: 'No program loaded' };
        const foldName = FOLDS[_state.fold];
        const fold = _foldData[foldName];
        if (!fold) return { error: 'Fold not found: ' + foldName };

        _state.tick++;
        const result = executeFold(foldName, fold, input);
        emit('fold', { phase: foldName, tick: _state.tick, result: result });

        // Advance to next fold
        _state.fold = (_state.fold + 1) % FOLDS.length;

        // Check for collapse (Xul)
        if (foldName === "Xul" || foldName === "Ch'en") {
            // If the result has `action: "collapse"`, stop
            if (result && result.action === 'collapse') {
                emit('complete', result);
                return { ...result, complete: true };
            }
        }

        return result;
    }

    /**
     * Run through all folds with given input.
     */
    function run(input) {
        const results = [];
        let current = input;
        for (let i = 0; i < FOLDS.length; i++) {
            current = step(current);
            results.push({ phase: FOLDS[_state.fold === 0 ? 5 : _state.fold - 1], output: current });
            if (current && current.complete) break;
        }
        emit('run', results);
        return results;
    }

    function executeFold(name, fold, input) {
        const output = { phase: name, input: input, nodes: [], action: 'continue' };
        (fold.nodes || []).forEach(node => {
            const result = evaluateNode(node, input, _state);
            output.nodes.push({ type: node.type, target: node.target, result: result });
            if (node.target) _state.context[node.target] = result;
        });
        // Xul fold: check for collapse condition
        if (name === 'Xul') {
            const collapseNode = fold.nodes.find(n => n.type === 'call' && n.name === 'collapse');
            if (collapseNode) output.action = 'collapse';
        }
        return output;
    }

    function evaluateNode(node, input, state) {
        switch (node.type) {
            case 'literal': return node.value;
            case 'ref': return state.context[node.name] || input[node.name] || null;
            case 'assign': return evaluateNode(node.value, input, state);
            case 'op': return evaluateOp(node.op, node.args, input, state);
            case 'if': return evaluateIf(node, input, state);
            case 'call': return evaluateCall(node, input, state);
            case 'emit': return evaluateNode(node.value, input, state);
            default: return null;
        }
    }

    function evaluateOp(op, args, input, state) {
        const vals = (args || []).map(a => evaluateNode(a, input, state));
        switch (op) {
            case '==': return vals[0] === vals[1];
            case '>':  return parseFloat(vals[0]) > parseFloat(vals[1]);
            case '<':  return parseFloat(vals[0]) < parseFloat(vals[1]);
            case '+=': return (parseFloat(vals[0]) || 0) + (parseFloat(vals[1]) || 0);
            default: return vals;
        }
    }

    function evaluateIf(node, input, state) {
        const test = evaluateNode(node.test, input, state);
        if (test) {
            return (node.then && node.then.nodes || []).map(n => evaluateNode(n, input, state));
        } else if (node.else) {
            return (node.else.nodes || []).map(n => evaluateNode(n, input, state));
        }
        return null;
    }

    function evaluateCall(node, input, state) {
        // Bridge to host: calls window.external methods
        if (typeof window !== 'undefined' && window.external) {
            try {
                const args = (node.args || []).map(a => evaluateNode(a, input, state));
                const result = window.external.KuhulCall(node.name, JSON.stringify(args));
                return result ? JSON.parse(result) : null;
            } catch (e) {
                return { error: e.message };
            }
        }
        return { call: node.name };
    }

    function emit(type, data) {
        if (typeof window !== 'undefined' && window.external) {
            try { window.external.KuhulEvent(type, JSON.stringify(data)); } catch (e) { }
        }
    }

    function getState() { return { ..._state, program: _program ? _program.meta : null }; }
    function reset() { _state = { fold: 0, tick: 0, memory: {}, context: {} }; }

    // Module export
    window.KuhulRuntime = { load, run, step, getState, reset, FOLDS };

})();
