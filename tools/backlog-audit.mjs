#!/usr/bin/env node
/*
    backlog-audit.mjs - one grep-count per defect in docs/backlog-from-upstream-plans.md.

    WHY THIS IS NOT A SHELL SCRIPT. A shell EOL/marker census on this machine has
    already reported a confident wrong answer: `$'\r'` collapses to the EMPTY
    pattern inside `$( )` and `grep -c ''` returns the line count, so the probe
    could not fail. Byte-level and count-level properties are measured in Node
    here for that reason.

    WHAT IT IS. Each entry names a defect, the files it lives in, and a PRESENT
    predicate. The audit prints, per defect, how many sites still match. A defect
    is CLOSED when its count reaches its target (normally 0).

    THE RED PATH IS THE POINT. Run this BEFORE any fix: every open defect must
    report a NON-ZERO count. A probe that reports 0 on known-bad input is a broken
    probe, not a fixed defect, and this file exists to make that distinguishable.

        node tools/backlog-audit.mjs            # table
        node tools/backlog-audit.mjs --json     # machine-readable
        node tools/backlog-audit.mjs --sites    # every matching file:line
*/

import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const REPO = join(fileURLToPath(new URL('.', import.meta.url)), '..');

// ---------------------------------------------------------------- file walking

function walk(dir, filter, out = []) {
    let entries;
    try { entries = readdirSync(dir); } catch { return out; }
    for (const e of entries) {
        if (e === 'bin' || e === 'obj' || e === 'node_modules' || e === '.git') continue;
        const p = join(dir, e);
        const st = statSync(p);
        if (st.isDirectory()) walk(p, filter, out);
        else if (filter(p)) out.push(p);
    }
    return out;
}

const csFiles = (sub) => walk(join(REPO, sub), (p) => p.endsWith('.cs'));
const rel = (p) => relative(REPO, p).split(sep).join('/');

/**
 * Strip C# comments from a line for MATCHING purposes only.
 *
 * WHY THIS EXISTS. Without it, this audit reports a defect as OPEN because the
 * COMMENT EXPLAINING THE FIX mentions the defective code. Measured: after P1-7
 * was fixed, the P1-7 probe matched the line
 *     // P1-7. Assembly.LoadFrom() held an open file handle on the DLL for
 * and after defect 26 was fixed, its probe matched
 *     // get_Parameter(BuiltInParameter.VIEW_SCALE)?.Set(Scale),
 * Both reported OPEN against correct code. A probe whose red path is proven but
 * whose GREEN path is not is only half an instrument.
 *
 * Line-level and therefore approximate: a defect written inside a block comment
 * spanning lines would still match. That is the safe direction - a false OPEN is
 * investigated, a false CLOSED ships.
 */
const stripComments = (line) => line.replace(/\/\/.*$/, '').replace(/\/\*.*?\*\//g, '');

/** Every line of `file` whose CODE (comments removed) matches `re`. */
function linesMatching(file, re) {
    const hits = [];
    const text = readFileSync(file, 'utf8');
    const lines = text.split(/\r?\n/);
    for (let i = 0; i < lines.length; i++) {
        re.lastIndex = 0;
        if (re.test(stripComments(lines[i]))) hits.push({ file: rel(file), line: i + 1, text: lines[i].trim() });
    }
    return hits;
}

/** Scan a set of files for a per-line regex. */
function scanLines(files, re) {
    return files.flatMap((f) => linesMatching(f, re));
}

/** Scan whole-file (multi-line aware) — one hit per file that matches. */
function scanFiles(files, predicate) {
    return files
        .filter((f) => predicate(readFileSync(f, 'utf8'), f))
        .map((f) => ({ file: rel(f), line: 0, text: '(whole-file predicate)' }));
}

// ------------------------------------------------------------------- the probes

const services = () => csFiles('commandset/Services');
const commands = () => csFiles('commandset/Commands');
const core = () => csFiles('plugin/Core');

/**
 * A FilteredElementCollector construction that is NOT disposed.
 *
 * TWO fixed shapes exist and BOTH count as closed - the first version of this
 * predicate knew only the first, and reported 12 correctly-fixed sites as open:
 *
 *   using (var c = new FilteredElementCollector(doc)) { ... }   using STATEMENT
 *   using var c = new FilteredElementCollector(doc);            using DECLARATION (C# 8)
 *   using FilteredElementCollector c = new FilteredElementCollector(doc);
 *
 * The declaration form is the better fix where the variable is not reassigned:
 * it disposes at end of scope while changing no braces and no indentation.
 */
const FEC_UNWRAPPED = (line) =>
    /new\s+FilteredElementCollector\s*\(/.test(line) &&
    !/^\s*using\s*\(/.test(line) &&        // using STATEMENT
    !/^\s*using\s+\S/.test(line);          // using DECLARATION ('using var' / 'using Type')

const DEFECTS = [
    {
        id: 'P0-2',
        title: 'Background-thread Revit API access — no structural guard',
        target: 0,
        probe: () => {
            // CLOSED when the socket path can both TELL whether it is on the Revit API
            // thread and ASSERT it for any code that bypasses ExternalEvent.
            //
            // Deliberately NOT looking for a dispatch wrapper: marshalling the whole
            // command.Execute onto the API thread deadlocks this architecture, because
            // the command then raises an ExternalEvent and waits for it from the very
            // thread that has to drain the queue. See CommandExecutor.ExecuteCommand.
            const canTell = scanLines(core(), /IsOnMainThread/);
            const canAssert = scanLines(core(), /RequireRevitApiThread/);
            const missing = [];
            if (!canTell.length) missing.push({ file: 'plugin/Core/CommandExecutor.cs', line: 0, text: 'no way to tell whether execution is on the Revit API thread' });
            if (!canAssert.length) missing.push({ file: 'plugin/Core/CommandExecutor.cs', line: 0, text: 'no assertion available to code that bypasses ExternalEvent' });
            return missing;
        },
    },
    {
        id: '65',
        title: 'set_view_properties — Set() on read-only VIEW_SCALE / MODEL_GRAPHICS_STYLE',
        target: 0,
        probe: () => scanLines(
            csFiles('commandset/Services/Views').filter((f) => f.endsWith('SetViewPropertiesEventHandler.cs')),
            /get_Parameter\s*\(\s*BuiltInParameter\.(VIEW_SCALE|MODEL_GRAPHICS_STYLE)\s*\)\s*\??\.\s*Set\s*\(/),
    },
    {
        id: 'P0-1',
        title: 'CreateWall — no curved-wall path (WallCreationInfo has no arc geometry)',
        target: 0,
        probe: () => scanFiles(
            csFiles('commandset/Models/Architecture').filter((f) => f.endsWith('WallCreationInfo.cs')),
            (t) => !/MidPoint/.test(t)),
    },
    {
        id: 'P0-4',
        title: 'CreateRoof — no extrusion branch; slope disabled instead of set',
        target: 0,
        probe: () => scanLines(
            csFiles('commandset/Services/Architecture').filter((f) => f.endsWith('CreateRoofEventHandler.cs')),
            /set_DefinesSlope\s*\([^,]+,\s*false\s*\)/),
    },
    {
        id: 'P1-3',
        title: 'REFUTED - Regenerate() REQUIRES a transaction; probe now asserts it is inside one',
        target: 0,
        // THE INHERITED CLAIM IS BACKWARDS AND ACTING ON IT WOULD BREAK THE CODE.
        //
        // The upstream planning document called doc.Regenerate() inside an open
        // Transaction a defect and prescribed commit -> Regenerate -> reopen. The
        // Revit API documentation for Document.Regenerate says the opposite:
        //
        //   InvalidOperationException - "Modification of the document is forbidden.
        //   Typically, this is because there is no open transaction"
        //
        // Regenerate IS a document modification. The prescribed fix throws on every
        // call. At one of the sites it would also destroy the result:
        // CreateStructuralFramingSystemEventHandler calls Regenerate specifically so
        // the BeamSystem materialises its member beams, and reads GetBeamIds() on the
        // next line - committing first would commit a half-built system.
        //
        // So this probe is INVERTED. It now reports a Regenerate call that is NOT
        // inside a transaction, which is the condition that actually throws.
        probe: () => {
            const hits = [];
            for (const f of services()) {
                const lines = readFileSync(f, 'utf8').split(/\r?\n/);
                let depth = 0; // open-transaction depth by Start()/Commit()/RollBack()
                for (let i = 0; i < lines.length; i++) {
                    const L = stripComments(lines[i]);
                    if (/\.\s*Start\s*\(/.test(L) && /trans|tran|transaction|tx\b/i.test(L)) depth++;

                    // A Commit/RollBack on an EARLY-EXIT branch does not close the
                    // transaction for the code that follows it in FILE order - that
                    // branch returns, and the surrounding transaction is still open.
                    //
                    // Measured: without this, both TagRooms and TagWalls reported a
                    // Regenerate with "no open transaction" because an error branch a
                    // few lines above read 'tran.RollBack(); return;'. Transaction
                    // depth is a control-flow property and this is a linear scan, so
                    // the look-ahead is a heuristic, not a decision procedure. It errs
                    // toward NOT reporting, because the alternative is crying wolf on
                    // correct code - and the compiler and the live smoke both cover
                    // the genuine no-transaction case anyway.
                    if (/\.\s*(Commit|RollBack)\s*\(/.test(L) && /trans|tran|transaction|tx\b/i.test(L)) {
                        const followedByReturn = lines
                            .slice(i + 1, i + 4)
                            .some((n) => /^\s*(return|continue|break|throw)\b/.test(stripComments(n)));
                        if (!followedByReturn) depth = Math.max(0, depth - 1);
                    }
                    if (/\.\s*Regenerate\s*\(\s*\)/.test(L) && depth === 0)
                        hits.push({ file: rel(f), line: i + 1, text: lines[i].trim() + '  -> Regenerate with NO open transaction: throws InvalidOperationException' });
                }
            }
            return hits;
        },
    },
    {
        id: 'P1-4',
        title: 'Empty / comment-only catch blocks swallow every exception',
        target: 0,
        probe: () => {
            const files = core();
            const hits = [];
            for (const f of files) {
                const text = readFileSync(f, 'utf8');
                const lines = text.split(/\r?\n/);
                for (let i = 0; i < lines.length; i++) {
                    if (!/^\s*catch\s*(\(|\{|$)/.test(lines[i])) continue;
                    // Collect the body between the catch's { and its matching }.
                    let j = i, brace = -1, body = [];
                    for (; j < lines.length && j < i + 40; j++) {
                        if (brace === -1 && lines[j].includes('{')) { brace = 0; }
                        if (brace === -1) continue;
                        for (const ch of lines[j]) {
                            if (ch === '{') brace++;
                            else if (ch === '}') brace--;
                        }
                        if (j > i || lines[j].includes('{')) body.push(lines[j]);
                        if (brace === 0) break;
                    }
                    const inner = body.join('\n')
                        .replace(/^[^{]*\{/, '').replace(/\}[^}]*$/, '')
                        .replace(/\/\/[^\n]*/g, '')          // line comments
                        .replace(/\/\*[\s\S]*?\*\//g, '')     // block comments
                        .trim();
                    const onlyFlag = /^_isRunning\s*=\s*false\s*;$/.test(inner);
                    if (inner === '' || onlyFlag)
                        hits.push({ file: rel(f), line: i + 1, text: lines[i].trim() + '  -> body: ' + JSON.stringify(inner) });
                    i = j;
                }
            }
            return hits;
        },
    },
    {
        id: 'P1-7',
        title: 'Assembly.LoadFrom() locks the plugin DLL for the AppDomain lifetime',
        target: 0,
        probe: () => scanLines(core(), /Assembly\s*\.\s*LoadFrom\s*\(/),
    },
    {
        id: 'P1-1',
        title: 'FilteredElementCollector constructed without a using block',
        target: 0,
        // Resolved per STATEMENT, not per line. A construction can sit on a
        // continuation line of a multi-line declaration:
        //     using FilteredElementCollector collector = filterByView
        //         ? new FilteredElementCollector(doc, doc.ActiveView.Id)
        //         : new FilteredElementCollector(doc);
        // where the 'using' is two lines above the 'new'. A line-based predicate
        // called that OPEN against correct code (measured: 2 sites).
        probe: () => {
            const hits = [];
            for (const f of services().concat(commands())) {
                const lines = readFileSync(f, 'utf8').split(/\r?\n/);
                for (let i = 0; i < lines.length; i++) {
                    const code = stripComments(lines[i]);
                    if (!/new\s+FilteredElementCollector\s*\(/.test(code)) continue;

                    // Walk back to the first line of this statement: the previous
                    // line ended the last one if it closed with ; { or }.
                    let start = i;
                    while (start > 0) {
                        const prev = stripComments(lines[start - 1]).trimEnd();
                        if (prev === '' || /[;{}]$/.test(prev)) break;
                        start--;
                    }
                    if (!FEC_UNWRAPPED(stripComments(lines[start]))) continue;   // disposed
                    if (start !== i && !FEC_UNWRAPPED(code)) continue;
                    hits.push({ file: rel(f), line: i + 1, text: lines[i].trim() });
                }
            }
            return hits;
        },
    },
    {
        id: 'P1-5',
        title: 'TaskDialog.Show() compiled into release builds (SayHello)',
        target: 0,
        probe: () => {
            const files = services().filter((f) => f.endsWith('SayHelloEventHandler.cs'));
            const hits = [];
            for (const f of files) {
                const text = readFileSync(f, 'utf8');
                if (!/TaskDialog\s*\.\s*Show\s*\(/.test(text)) continue;
                if (/#if\s+DEBUG/.test(text)) continue;   // stripped from release: closed
                hits.push(...linesMatching(f, /TaskDialog\s*\.\s*Show\s*\(/));
            }
            return hits;
        },
    },
    {
        id: 'P2-3',
        title: 'Redundant static _executionLock serialises unrelated requests',
        target: 0,
        probe: () => scanLines(commands(), /static\s+readonly\s+object\s+_executionLock/),
    },
    {
        id: 'P2-4',
        title: 'Exception caught then discarded — only a hardcoded string reported',
        target: 0,
        probe: () => {
            const files = services().filter((f) => f.endsWith('GetCurrentViewInfoEventHandler.cs'));
            const hits = [];
            for (const f of files) {
                const lines = readFileSync(f, 'utf8').split(/\r?\n/);
                for (let i = 0; i < lines.length; i++) {
                    if (!/catch\s*\(\s*Exception\s+(\w+)\s*\)/.test(lines[i])) continue;
                    const name = lines[i].match(/catch\s*\(\s*Exception\s+(\w+)\s*\)/)[1];
                    const body = lines.slice(i + 1, i + 12).join('\n');
                    const re = new RegExp('\\b' + name + '\\b');
                    if (!re.test(body)) hits.push({ file: rel(f), line: i + 1, text: lines[i].trim() + '  -> ' + name + ' never referenced' });
                }
            }
            return hits;
        },
    },
    {
        id: 'P2-1',
        title: 'ManualResetEvent fields never disposed (kernel handle leak)',
        target: 0,
        probe: () => {
            // A handler is CLOSED when it derives from a disposing base OR implements IDisposable.
            //
            // The field is declared TWO ways and this probe must see both:
            //     private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);
            //     private readonly ManualResetEvent _resetEvent = new(false);   // C# 9 target-typed
            // Matching only `new ManualResetEvent(` reported P2-1 CLOSED while 13
            // handlers were still leaking the handle. Match on the FIELD TYPE,
            // which both spellings share, rather than on the initialiser.
            const files = services().filter((f) => /\bManualResetEvent\s+\w+\s*=/.test(readFileSync(f, 'utf8')));
            return files
                .filter((f) => {
                    const t = readFileSync(f, 'utf8');
                    return !/IDisposable/.test(t) && !/WaitableEventHandlerBase/.test(t);
                })
                .map((f) => ({ file: rel(f), line: 0, text: 'ManualResetEvent field, no IDisposable and no disposing base' }));
        },
    },
    {
        id: 'P2-5',
        title: 'Command parameter-set / raise / read cycle unguarded (shared handler race)',
        target: 0,
        probe: () => {
            // CLOSED when the raise-and-read cycle is guarded, by a lock, a semaphore,
            // or a base class that owns the guard.
            const files = commands().filter((f) => /RaiseAndWaitForCompletion|_handler\s*\.\s*SetParameters/.test(readFileSync(f, 'utf8')));
            return files
                .filter((f) => {
                    const t = readFileSync(f, 'utf8');
                    return !/_executionLock|SemaphoreSlim|GuardedCommandBase|ExecuteGuarded/.test(t);
                })
                .map((f) => ({ file: rel(f), line: 0, text: 'SetParameters/Raise/Result cycle with no guard' }));
        },
    },
    {
        id: 'P2-3D-TAG-MSG',
        title: 'Tagging in a 3D view raises raw Revit text, or a misleading success',
        target: 0,
        probe: () => {
            const targets = ['CreateTagEventHandler.cs', 'TagWallsEventHandler.cs'];
            const files = services().filter((f) => targets.some((t) => f.endsWith(t)));
            return files
                .filter((f) => !/ViewType\s*\.\s*ThreeD|is\s+View3D/.test(readFileSync(f, 'utf8')))
                .map((f) => ({ file: rel(f), line: 0, text: 'IndependentTag.Create with no view-dimensionality guard' }));
        },
    },
    {
        id: '6',
        title: 'create_ramp advertises a capability that always throws',
        target: 0,
        probe: () => {
            const ts = join(REPO, 'server/src/tools/create_ramp.ts');
            let t;
            try { t = readFileSync(ts, 'utf8'); } catch { return []; }   // removed outright = closed
            return /not supported|no public ramp/i.test(t)
                ? []
                : [{ file: 'server/src/tools/create_ramp.ts', line: 0, text: 'description claims capability; handler throws NotSupportedException' }];
        },
    },
    {
        id: '9',
        title: 'create_opening — schema says "Wall", enum member is "WallOpening"',
        target: 0,
        probe: () => {
            const ts = join(REPO, 'server/src/tools/create_opening.ts');
            const cs = join(REPO, 'commandset/Models/Architecture/OpeningCreationInfo.cs');
            let tsT, csT;
            try { tsT = readFileSync(ts, 'utf8'); csT = readFileSync(cs, 'utf8'); } catch { return []; }
            const converted = /JsonConverter|StringEnumConverter|EnumMember/.test(csT);
            const schemaSaysBare = /"?Opening type:\s*Wall,\s*Floor/i.test(tsT) || /Wall,\s*Floor,\s*Roof,?\s*(or\s*)?Shaft/.test(tsT);
            const enumIsSuffixed = /WallOpening/.test(csT);
            return (schemaSaysBare && enumIsSuffixed && !converted)
                ? [{ file: 'server/src/tools/create_opening.ts', line: 0, text: 'schema value set does not deserialise to the C# enum' }]
                : [];
        },
    },
    {
        id: '26',
        title: 'create_drafting_view — unguarded Set() on read-only VIEW_SCALE',
        target: 0,
        probe: () => scanLines(
            csFiles('commandset/Services/Views').filter((f) => f.endsWith('CreateDraftingViewEventHandler.cs')),
            /get_Parameter\s*\(\s*BuiltInParameter\.VIEW_SCALE\s*\)\s*\??\.\s*Set\s*\(/),
    },
    {
        id: '27',
        title: 'create_view — VIEW_SCALE written with no IsReadOnly guard',
        target: 0,
        probe: () => scanLines(
            csFiles('commandset/Services/Views').filter((f) => f.endsWith('CreateViewEventHandler.cs')),
            /get_Parameter\s*\(\s*BuiltInParameter\.VIEW_SCALE\s*\)\s*\??\.\s*Set\s*\(/),
    },
    {
        id: '33',
        title: 'create_tag in a 3D view — opaque Revit error, not the required wording',
        target: 0,
        probe: () => services()
            .filter((f) => f.endsWith('CreateTagEventHandler.cs'))
            .filter((f) => !/only be created in a 2D view/i.test(readFileSync(f, 'utf8')))
            .map((f) => ({ file: rel(f), line: 0, text: 'no plain-language 2D-view message' })),
    },
    {
        id: '35',
        title: 'create_revision — SetRevisionNumber with no NumberType guard',
        target: 0,
        probe: () => {
            const files = services().filter((f) => f.endsWith('CreateRevisionEventHandler.cs'));
            return files
                .filter((f) => {
                    const t = readFileSync(f, 'utf8');
                    return /SetRevisionNumber\s*\(/.test(t) && !/NumberType/.test(t);
                })
                .map((f) => ({ file: rel(f), line: 0, text: 'SetRevisionNumber called with NumberType never inspected or set' }));
        },
    },
    {
        id: '54',
        title: 'connect_mep — ConnectTo with no MEP domain compatibility check',
        target: 0,
        probe: () => services()
            .filter((f) => f.endsWith('ConnectMEPEventHandler.cs'))
            .filter((f) => !/\.\s*Domain\b/.test(readFileSync(f, 'utf8')))
            .map((f) => ({ file: rel(f), line: 0, text: 'ConnectTo switch with no Connector.Domain pre-flight' })),
    },
];

// ------------------------------------------------------------------------ main

const args = process.argv.slice(2);
const asJson = args.includes('--json');
const showSites = args.includes('--sites');

const results = DEFECTS.map((d) => {
    let sites = [];
    let error = null;
    try { sites = d.probe(); } catch (e) { error = String(e && e.stack || e); }
    return { id: d.id, title: d.title, target: d.target, count: sites.length, sites, error };
});

if (asJson) {
    console.log(JSON.stringify(results, null, 2));
} else {
    const open = results.filter((r) => r.count > r.target);
    const closed = results.filter((r) => r.count <= r.target && !r.error);
    const broken = results.filter((r) => r.error);

    console.log('');
    console.log('  BACKLOG AUDIT - ' + DEFECTS.length + ' defects from docs/backlog-from-upstream-plans.md');
    console.log('  ' + '='.repeat(76));
    for (const r of results) {
        const state = r.error ? 'PROBE-ERR' : (r.count > r.target ? 'OPEN ' : 'CLOSED');
        const n = String(r.count).padStart(4);
        console.log(`  ${state}  ${n} site(s)  ${r.id.padEnd(14)} ${r.title}`);
        if (r.error) console.log('           ' + r.error.split('\n')[0]);
        if (showSites) for (const s of r.sites) console.log(`             ${s.file}:${s.line}  ${s.text.slice(0, 110)}`);
    }
    console.log('  ' + '='.repeat(76));
    console.log(`  OPEN ${open.length}   CLOSED ${closed.length}   PROBE-ERR ${broken.length}`);
    console.log('');
    if (broken.length) process.exitCode = 2;
    else if (open.length) process.exitCode = 1;
}
