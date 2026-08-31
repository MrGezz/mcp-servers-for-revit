#!/usr/bin/env node
/*
    sweep-handler-lifecycle.mjs - the three UNIFORM defects, applied mechanically.

    P2-1  ManualResetEvent fields are never disposed (72 handlers, one identical
          field declaration each).
    P2-5  The SetParameters / Raise / read cycle is unguarded, so two concurrent
          requests for the same command share one handler instance and overwrite
          each other's parameters (81 commands).
    P2-3  Five commands carry a STATIC lock, which serialises unrelated commands
          against each other while still not fixing P2-5 for the other 81.

    WHY A SCRIPT AND NOT AN AGENT. These three are uniform to the character: 72
    files carry byte-identical field declarations and 85 carry byte-identical
    class declarations. A deterministic transform with a matched-count assertion
    is both safer and more auditable than 150 separate judgement calls.

    THE RACE THIS ALSO CLOSES, WHICH THE BACKLOG DID NOT NAME
    ---------------------------------------------------------
    Every handler reads:

        public bool WaitForCompletion(int timeoutMilliseconds = 15000)
        {
            _resetEvent.Reset();                          // <-- after Raise()
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

    The command calls SetParameters() - which already Resets, on the calling
    thread, BEFORE the event is raised - then RaiseAndWaitForCompletion(), which
    raises and then calls the method above. If Execute() completes on the Revit
    UI thread before that second Reset() lands, the completion Set() is ERASED,
    WaitOne blocks for the whole timeout, and the command throws TimeoutException
    for work that actually SUCCEEDED.

    Deleting the second Reset() is the entire fix. SetParameters keeps the
    correct one. The per-handler default timeouts (10000/12500/15000/30000/60000)
    are deliberately left alone - they are not part of the defect.

    SAFETY
    ------
    Dry run by default; --apply writes. Every transform asserts its match count
    before writing, refuses a file it does not recognise, preserves CRLF, and
    re-reads what it wrote. The build is the real proof and runs afterwards.

        node tools/sweep-handler-lifecycle.mjs            # report only
        node tools/sweep-handler-lifecycle.mjs --apply
*/

import { readFileSync, writeFileSync, readdirSync, statSync, existsSync, mkdirSync } from 'node:fs';
import { join, relative, sep, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const REPO = join(fileURLToPath(new URL('.', import.meta.url)), '..');
const APPLY = process.argv.includes('--apply');

function walk(dir, out = []) {
    let entries;
    try { entries = readdirSync(dir); } catch { return out; }
    for (const e of entries) {
        if (['bin', 'obj', 'node_modules', '.git'].includes(e)) continue;
        const p = join(dir, e);
        statSync(p).isDirectory() ? walk(p, out) : (p.endsWith('.cs') && out.push(p));
    }
    return out;
}
const rel = (p) => relative(REPO, p).split(sep).join('/');

/** Count occurrences of a literal or regex, for match-count assertions. */
const count = (text, re) => (text.match(re) || []).length;

/** Assert a file kept CRLF and gained no lone LF. */
function assertCrlf(path, text) {
    const lone = (text.match(/(?<!\r)\n/g) || []).length;
    if (lone !== 0) throw new Error(`${rel(path)}: ${lone} LF line ending(s) introduced into a CRLF file`);
}

const report = { p21: [], p25: [], p23: [], guard: [], refused: [], base: null };

// ===========================================================================
// 0. The base class. Deliberately does NOT declare IExternalEventHandler or
//    IWaitableExternalEventHandler: if it did, it would have to declare Execute
//    and GetName abstract, and then all 72 handlers would need `override` added
//    to two more members each. Leaving the interfaces on the derived class means
//    the ONLY per-file change is the base name and the deleted field.
// ===========================================================================

const BASE_PATH = join(REPO, 'commandset', 'Utils', 'WaitableEventHandlerBase.cs');
const BASE_SRC = `using System;
using System.Threading;

namespace RevitMCPCommandSet.Utils
{
    /// <summary>
    /// Owns the completion signal every waitable external-event handler needs,
    /// and disposes it.
    ///
    /// WHY THIS EXISTS. Every handler in this command set declared its own
    /// <see cref="ManualResetEvent"/> and none of them disposed it. A
    /// ManualResetEvent holds a Win32 kernel event handle; one handler instance
    /// is created per command and lives for the whole Revit session, so the
    /// handles accumulated for the life of the process.
    ///
    /// This type deliberately does NOT implement IExternalEventHandler or
    /// IWaitableExternalEventHandler. Declaring them here would force Execute()
    /// and GetName() to be abstract, and every derived handler would then need
    /// an 'override' keyword added to both. Leaving the interfaces on the
    /// derived classes keeps the change to each handler down to its base name
    /// and one deleted field.
    /// </summary>
    public abstract class WaitableEventHandlerBase : IDisposable
    {
        /// <summary>
        /// Signalled by the handler when Execute() finishes; waited on by the
        /// calling thread. Protected rather than private because the derived
        /// handlers Set() and Reset() it directly, exactly as they did when they
        /// each declared their own.
        /// </summary>
        protected readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        private bool _disposed;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;
            if (disposing)
            {
                // Release any thread parked in WaitForCompletion before the handle
                // goes away, so disposal cannot strand a caller for its timeout.
                try { _resetEvent.Set(); } catch (ObjectDisposedException) { }
                _resetEvent.Dispose();
            }
        }

        ~WaitableEventHandlerBase()
        {
            Dispose(false);
        }
    }
}
`.replace(/\r?\n/g, '\r\n');

// ===========================================================================
// 1. P2-1 + the WaitForCompletion race, per handler.
// ===========================================================================

// BOTH initialiser spellings. 72 handlers write the explicit
// `= new ManualResetEvent(false)`; 13 use the C# 9 TARGET-TYPED form
// `= new(false)`. The first version of this pattern knew only the explicit one,
// so those 13 kept an undisposed kernel handle while the audit reported P2-1
// CLOSED - the probe was blind in exactly the same way.
const FIELD_RE = /^[ \t]*private readonly ManualResetEvent _resetEvent = new(?: ManualResetEvent)?\(false\);[ \t]*\r?\n/m;
const CLASS_RE = /(\r?\n[ \t]*public class (\w+) )(: IExternalEventHandler, IWaitableExternalEventHandler)/;
// The second Reset(): the one INSIDE WaitForCompletion, not the one in SetParameters.
//
// THE PARAMETER NAME IS NOT FIXED, AND ASSUMING IT WAS COST 12 HANDLERS.
// The first version of this pattern hardcoded `int timeoutMilliseconds = \d+`.
// Measured across commandset/Services: 73 handlers spell it `timeoutMilliseconds`
// and 12 spell it `timeout`. Those 12 - all of Modify/ and Query/ - got the base
// class and the disposal but silently kept the race, and because the file still
// matched FIELD_RE it was never reported as refused either. A transform that
// half-applies and says nothing is worse than one that refuses out loud.
//
// Now: any identifier, default value optional.
const WAIT_RE  = /(public bool WaitForCompletion\(\s*int\s+\w+(?:\s*=\s*\d+)?\s*\)\r?\n([ \t]*)\{\r?\n)([ \t]*)_resetEvent\.Reset\(\);[ \t]*\r?\n/;

// Recognises the declaration regardless of whether the body still resets, so a
// handler this script CANNOT fully process is reported rather than passed over.
const WAIT_DECL_RE = /public bool WaitForCompletion\s*\(/;

function sweepHandlers() {
    const files = walk(join(REPO, 'commandset', 'Services'));
    for (const f of files) {
        const orig = readFileSync(f, 'utf8');
        if (!FIELD_RE.test(orig) && !WAIT_RE.test(orig) && !WAIT_DECL_RE.test(orig)) continue;

        let text = orig;
        const actions = [];

        // A handler that declares WaitForCompletion, still resets inside it, and
        // does NOT match the shape this script rewrites is a REFUSAL, not a
        // silent skip. This is the check that would have caught the 12.
        if (WAIT_DECL_RE.test(orig) && !WAIT_RE.test(orig)) {
            const decl = /public bool WaitForCompletion[^\n]*\r?\n[ \t]*\{\r?\n([\s\S]{0,200}?)\}/.exec(orig);
            if (decl && /_resetEvent\.Reset\(\)/.test(decl[1])) {
                report.refused.push(`${rel(f)}: WaitForCompletion still resets but does not match the rewrite shape - NOT fixed`);
            }
        }

        // -- the unguarded WaitForCompletion Reset -----------------------------
        if (WAIT_RE.test(text)) {
            // Same lesson as WAIT_RE: count the DECLARATION, not one spelling of
            // its parameter. Hardcoding `timeoutMilliseconds` here made the guard
            // report "0 declarations, expected 1" for the 12 handlers that spell
            // it `timeout`, refusing files the pattern above had just matched.
            const n = count(text, /public bool WaitForCompletion\s*\(/g);
            if (n !== 1) { report.refused.push(`${rel(f)}: ${n} WaitForCompletion declarations, expected 1`); continue; }
            text = text.replace(WAIT_RE, '$1');
            actions.push('race');
        }

        // -- the undisposed field ---------------------------------------------
        if (FIELD_RE.test(text)) {
            // Count the FIELD, not one spelling of its initialiser - same lesson
            // as WAIT_RE and FIELD_RE above. Three guards in this file have now
            // been caught assuming a single spelling of the same construct.
            const n = count(text, /private readonly ManualResetEvent _resetEvent\s*=/g);
            if (n !== 1) { report.refused.push(`${rel(f)}: ${n} _resetEvent fields, expected 1`); continue; }
            if (!CLASS_RE.test(text)) {
                report.refused.push(`${rel(f)}: has the field but not the expected class declaration - left alone`);
                continue;
            }
            text = text.replace(FIELD_RE, '');
            text = text.replace(CLASS_RE, '$1: WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler');
            if (!/using RevitMCPCommandSet\.Utils;/.test(text)) {
                // Put it beside the other RevitMCPCommandSet usings, or at the top.
                if (/^using RevitMCPCommandSet\.[^\r\n]*;\r?\n/m.test(text)) {
                    text = text.replace(/^(using RevitMCPCommandSet\.[^\r\n]*;\r?\n)/m, 'using RevitMCPCommandSet.Utils;\r\n$1');
                } else {
                    text = text.replace(/^(﻿?)/, '$1using RevitMCPCommandSet.Utils;\r\n');
                }
            }
            actions.push('dispose');
        }

        if (text === orig) continue;
        assertCrlf(f, text);
        report.p21.push({ file: rel(f), actions });
        if (APPLY) {
            writeFileSync(f, text);
            const back = readFileSync(f, 'utf8');
            if (back !== text) throw new Error(`${rel(f)}: read-back differs from what was written`);
        }
    }
}

// ===========================================================================
// 2. P2-5 + P2-3, per command.
//
//    The guard is INSTANCE level, not static. A static lock (the shape five
//    commands already carry) serialises every request for EVERY command against
//    every other, which is the P2-3 complaint, while still leaving the other 81
//    commands racing - so the two backlog items are one fix, not two opposed
//    ones. ExternalEvent.Raise() already serialises EXECUTION on the UI thread;
//    what is unprotected is the shared handler's PARAMETER SLOT between
//    SetParameters() and the handler reading it.
// ===========================================================================

const EXEC_RE = /([ \t]*)public override object Execute\(JObject parameters, string requestId\)\r?\n[ \t]*\{\r?\n/;

/** Index just past the matching close brace for the '{' at openIdx. */
function matchBrace(text, openIdx) {
    let depth = 0;
    let inStr = null, inCmt = null;
    for (let i = openIdx; i < text.length; i++) {
        const c = text[i], n = text[i + 1];
        if (inCmt === 'line') { if (c === '\n') inCmt = null; continue; }
        if (inCmt === 'block') { if (c === '*' && n === '/') { inCmt = null; i++; } continue; }
        if (inStr) {
            if (c === '\\' && inStr !== 'verbatim') { i++; continue; }
            if (inStr === 'verbatim' && c === '"' && n === '"') { i++; continue; }
            if ((inStr === 'str' || inStr === 'verbatim') && c === '"') inStr = null;
            else if (inStr === 'char' && c === "'") inStr = null;
            continue;
        }
        if (c === '/' && n === '/') { inCmt = 'line'; i++; continue; }
        if (c === '/' && n === '*') { inCmt = 'block'; i++; continue; }
        if (c === '@' && n === '"') { inStr = 'verbatim'; i++; continue; }
        if (c === '"') { inStr = 'str'; continue; }
        if (c === "'") { inStr = 'char'; continue; }
        if (c === '{') depth++;
        else if (c === '}') { depth--; if (depth === 0) return i; }
    }
    return -1;
}

function sweepCommands() {
    const files = walk(join(REPO, 'commandset', 'Commands'));
    for (const f of files) {
        const orig = readFileSync(f, 'utf8');
        let text = orig;
        const actions = [];

        // -- P2-3: demote the five static locks to instance locks --------------
        if (/static readonly object _executionLock/.test(text)) {
            text = text.replace(/private static readonly object _executionLock = new object\(\);/g,
                                'private readonly object _executionLock = new object();');
            actions.push('static->instance');
            report.p23.push(rel(f));
        }

        const m = EXEC_RE.exec(text);
        if (!m) { if (actions.length) { report.refused.push(`${rel(f)}: static lock demoted but no recognised Execute signature`); } continue; }

        const alreadyGuarded = /lock \(_executionLock\)/.test(text) || /_turnstile|SemaphoreSlim/.test(text);

        if (!alreadyGuarded) {
            const braceOpen = text.indexOf('{', m.index + m[0].length - 3);
            const braceClose = matchBrace(text, braceOpen);
            if (braceClose < 0) { report.refused.push(`${rel(f)}: could not match Execute's braces`); continue; }

            const indent = m[1];
            const body = text.slice(braceOpen + 1, braceClose);
            // Re-indent the body one level so the inserted lock block reads correctly.
            const bodyIndented = body.replace(/\r\n(?=[ \t]*\S)/g, '\r\n' + '    ');
            const wrapped =
                '\r\n' + indent + '    lock (_executionLock)\r\n' +
                indent + '    {' + bodyIndented.replace(/^\r\n/, '\r\n') +
                indent + '    }\r\n' + indent;
            text = text.slice(0, braceOpen + 1) + wrapped + text.slice(braceClose);
            actions.push('guard-body');
        }

        // -- the instance lock field, if the class does not have one ------------
        if (!/readonly object _executionLock/.test(text)) {
            // ':' may be followed by any run of whitespace - one file writes
            // 'class X :    ExternalEventCommandBase' and a single-space pattern
            // silently refused it.
            const cls = /(\r?\n([ \t]*)public class \w+\s*:\s*\w+[^\r\n]*\r?\n[ \t]*\{\r?\n)/.exec(text);
            if (!cls) { report.refused.push(`${rel(f)}: no recognised class declaration to add the guard field to`); continue; }
            const ind = cls[2] + '    ';
            const field =
                ind + '// Instance-level, not static: ExternalEvent.Raise() already serialises\r\n' +
                ind + '// EXECUTION on the Revit UI thread, so a static lock would serialise\r\n' +
                ind + '// unrelated commands against each other for no benefit. What is\r\n' +
                ind + '// unprotected is this command\'s SHARED HANDLER INSTANCE - the registry\r\n' +
                ind + '// keeps one per command name - between SetParameters() and the handler\r\n' +
                ind + '// reading those parameters on the UI thread.\r\n' +
                ind + 'private readonly object _executionLock = new object();\r\n\r\n';
            text = text.slice(0, cls.index + cls[1].length) + field + text.slice(cls.index + cls[1].length);
            actions.push('guard-field');
        }

        if (text === orig) continue;

        // -- assertions before writing -----------------------------------------
        const ob = count(text, /\{/g), cb = count(text, /\}/g);
        const ob0 = count(orig, /\{/g), cb0 = count(orig, /\}/g);
        if (ob - ob0 !== cb - cb0) { report.refused.push(`${rel(f)}: brace delta unbalanced (${ob - ob0} open vs ${cb - cb0} close)`); continue; }
        assertCrlf(f, text);

        report.guard.push({ file: rel(f), actions });
        if (APPLY) {
            writeFileSync(f, text);
            const back = readFileSync(f, 'utf8');
            if (back !== text) throw new Error(`${rel(f)}: read-back differs from what was written`);
        }
    }
}

// ============================================================================ main

if (APPLY) {
    mkdirSync(dirname(BASE_PATH), { recursive: true });
    writeFileSync(BASE_PATH, BASE_SRC);
    report.base = rel(BASE_PATH) + ' (written)';
} else {
    report.base = rel(BASE_PATH) + (existsSync(BASE_PATH) ? ' (exists)' : ' (would be created)');
}

sweepHandlers();
sweepCommands();

console.log('');
console.log('  HANDLER / COMMAND LIFECYCLE SWEEP   ' + (APPLY ? '*** APPLIED ***' : '(dry run - pass --apply to write)'));
console.log('  ' + '='.repeat(74));
console.log('  base class          : ' + report.base);
console.log('  P2-1 handlers       : ' + report.p21.length + '   (field removed, base class, WaitForCompletion race)');
console.log('     of which race-fix: ' + report.p21.filter((r) => r.actions.includes('race')).length);
console.log('  P2-3 static->instance: ' + report.p23.length);
console.log('  P2-5 commands guarded: ' + report.guard.length);
console.log('  REFUSED (left alone) : ' + report.refused.length);
for (const r of report.refused) console.log('      ! ' + r);
console.log('  ' + '='.repeat(74));
console.log('');
