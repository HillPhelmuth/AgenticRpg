---
name: simplify-csharp
description: Simplify and clean up C# by deduplicating similar code, extracting helper methods, and improving readability without changing behavior.
argument-hint: "Optional: scope=selection|file|files; files=src/**.cs; focus=dedupe,extract,readability"
agent: 'agent'
---

## Task
Refactor C# code to be simpler, cleaner, and less repetitive by consolidating similar logic into well-named helper methods and small abstractions, without changing externally observable behavior.

## Scope (pick the best match)
- If there is selected code, refactor ONLY the selection:
  ${selection}
- Otherwise, refactor the current file:
  ${file}
- Optional additional targets (globs, folders, filenames). If empty, ignore:
  ${input:files:Extra files or globs to include (e.g., src/**.cs, tests/**.cs)}

## Non-negotiables
- Preserve behavior and public API surface unless explicitly required by the refactor.
- Keep changes small, local, and reviewable. Prefer a few focused edits over a sweeping rewrite.
- Update all call sites when extracting or renaming private/internal members.
- Do not introduce new external dependencies/packages.
- Prefer clarity over cleverness. Avoid over-generalization.

## Refactoring goals (in priority order)
1. **Remove duplication**
   - Identify copy/paste blocks, near-duplicate branches, repeated argument validation, repeated logging, repeated mapping, repeated try/catch patterns.
   - Consolidate into helper methods, local functions, or private static methods.
   - If duplication spans multiple types, consider an internal helper class in the same namespace.

2. **Improve structure**
   - Extract small, single-purpose methods with intent-revealing names.
   - Convert deeply nested conditionals into guard clauses when it improves readability.
   - Prefer early returns over nested else blocks where appropriate.

3. **Make intent obvious**
   - Improve names of locals and private methods when they obscure meaning.
   - Reduce long methods by splitting into logical steps.
   - Replace magic strings/numbers with named constants where it adds clarity.

4. **Modern, idiomatic C#**
   - Use `ArgumentNullException.ThrowIfNull(...)` and related patterns for parameter validation where appropriate.
   - Use pattern matching and switch expressions when they improve readability (not for style points).
   - Use `var` only when the type is obvious from the right-hand side.

## Heuristics for combining similar code
- If two code paths differ only by a small detail, prefer:
  - A shared helper method with parameters for the varying parts.
  - A small strategy delegate (`Func<>`/`Action<>`) ONLY when it reduces complexity.
  - A data-driven approach (dictionary/map/table) for repetitive mapping.
- For repeated async call patterns, consider a helper that wraps:
  - common retries/timeouts/logging, but keep it straightforward and testable.

## Process
1. Briefly summarize what you think the code does (1-3 sentences).
2. List the top duplication hotspots you will address (bullets).
3. Apply edits:
   - Extract helpers
   - Replace duplicated blocks with calls
   - Improve naming and flow
4. Validate:
   - Ensure compilation is preserved conceptually
   - If tests exist in the workspace, update/adjust only if required by refactor (behavior must remain the same)

## Output
- Provide:
  - A concise summary of changes
  - A checklist of what to verify locally (build/test commands if obvious for .NET repos)
- If you changed multiple files, list them.
