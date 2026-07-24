# Copilot instructions for RumOverboard

- Use Graphify for repository navigation before broad manual source scanning.
- If `graphify-out/graph.json` exists, start with:
  - `graphify query "<question>"`
- For dependency or concept relationships, use:
  - `graphify path "<A>" "<B>"`
- For focused concept explanation, use:
  - `graphify explain "<concept>"`
- After modifying code, update graph data:
  - `graphify update .`
  - If graph is missing: `graphify extract . --code-only`

## Mechanics workflow

- Develop each ship mechanic in an isolated feature scene first.
- Every mechanic scene must include runtime debug controls, reset, and save-to-config.
- Bind cross-component dependencies through ship aggregator(s), not scattered direct wiring.
- Store each mechanic in its own feature directory (runtime, debug window, config, scene tooling together).
- Integrate into gameplay ship only after the isolated scene is validated.

