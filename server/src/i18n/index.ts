import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

/**
 * OPTIONAL LOCALISATION.
 *
 * The source of truth for every user-facing string in this project is ENGLISH, in
 * the code. This module adds translations on top, opt-in and off by default.
 *
 * Why it exists: this codebase was written in Simplified Chinese by its original
 * authors, and a large part of its value is in the wording of the tool
 * descriptions and messages they wrote. Converting the code to English should not
 * mean deleting that work, so the original strings are preserved here as a
 * translation catalogue rather than discarded.
 *
 * LANGUAGE TAG. The catalogue is `zh-Hans` - Chinese written in the SIMPLIFIED
 * script (BCP 47: `zh` + script subtag `Hans`). That is the accurate tag for text,
 * and it is not the same thing as "Mandarin": Mandarin (`cmn`) names a spoken
 * variety, and both Mandarin and Cantonese speakers write Hans or Hant. The
 * distinction that matters for a string table is the SCRIPT, so the tag is
 * `zh-Hans`. Measured on the original sources: the forms used were simplified
 * throughout, with no traditional-only characters.
 *
 * Selection: set REVIT_MCP_LOCALE=zh-Hans. Anything else, unset, or a missing
 * catalogue means English, with no error - a missing translation must never break
 * a tool.
 */

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

export const DEFAULT_LOCALE = "en";

export interface LocaleCatalogue {
  locale: string;
  language: string;
  script?: string;
  note?: string;
  strings: Record<string, string>;
}

let loaded: { locale: string; map: Record<string, string> } | null = null;

function localesDir(): string {
  // Resolves under build/ at runtime and src/ under ts-node; both are correct
  // relative to this module.
  return path.join(__dirname, "locales");
}

export function activeLocale(): string {
  const raw = (process.env.REVIT_MCP_LOCALE || "").trim();
  return raw || DEFAULT_LOCALE;
}

export function availableLocales(): string[] {
  try {
    return fs
      .readdirSync(localesDir())
      .filter((f) => f.endsWith(".json"))
      .map((f) => f.replace(/\.json$/, ""));
  } catch {
    return [];
  }
}

function catalogue(): Record<string, string> {
  const locale = activeLocale();
  if (loaded && loaded.locale === locale) return loaded.map;

  let map: Record<string, string> = {};
  if (locale !== DEFAULT_LOCALE) {
    const file = path.join(localesDir(), `${locale}.json`);
    try {
      const parsed = JSON.parse(fs.readFileSync(file, "utf8")) as LocaleCatalogue;
      map = parsed && parsed.strings ? parsed.strings : {};
    } catch (e) {
      // Say so once, then carry on in English. A translation problem must not
      // take the server down or silently look like a missing tool.
      console.error(
        `[i18n] locale "${locale}" requested but its catalogue could not be read ` +
          `(${e instanceof Error ? e.message : String(e)}). Falling back to English. ` +
          `Available: ${availableLocales().join(", ") || "(none)"}`
      );
      map = {};
    }
  }
  loaded = { locale, map };
  return map;
}

/**
 * Translate one English string. Returns the English unchanged when there is no
 * translation - deliberately, so an incomplete catalogue degrades to English
 * per-string instead of failing.
 */
export function t(english: string): string {
  if (!english) return english;
  const map = catalogue();
  const hit = map[english];
  return typeof hit === "string" && hit.length ? hit : english;
}

/** How much of the active catalogue actually covers the strings we ask for. */
export function localeStatus(): {
  active: string;
  available: string[];
  entries: number;
  isDefault: boolean;
} {
  const active = activeLocale();
  return {
    active,
    available: availableLocales(),
    entries: Object.keys(catalogue()).length,
    isDefault: active === DEFAULT_LOCALE,
  };
}

/** Test seam. */
export function _reset(): void {
  loaded = null;
}
