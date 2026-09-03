/**
 * Shared zod fragments.
 *
 * The tool catalogue is sent to the model on EVERY request, so a schema that
 * spells out "X coordinate of start point" three times per point, twice per
 * line, in twenty tools, is paid for twenty times per turn. These fragments
 * carry one short description on the object rather than one per number, and
 * the tool description states the unit once.
 */
import { z } from "zod";

/** {x, y, z} in millimetres. */
export const Pt = z.object({ x: z.number(), y: z.number(), z: z.number() });

/** A straight segment p0 -> p1 in millimetres. */
export const Line = z.object({ p0: Pt, p1: Pt });

/** Closed polygon given as consecutive vertices (mm). */
export const Polygon = z.array(Pt).min(3);

/** An integer Revit ElementId. */
export const ElementId = z.number().int();

/** ElementIds as a batch. */
export const ElementIds = z.array(ElementId).min(1);

/** 0-255 RGB triple. */
export const RGB = z.object({
  r: z.number().int().min(0).max(255),
  g: z.number().int().min(0).max(255),
  b: z.number().int().min(0).max(255),
});

/** Result-size control shared by every listing tool. */
export const Limit = (fallback: number) =>
  z.number().int().positive().max(500).optional().describe(`Max records (default ${fallback})`);
