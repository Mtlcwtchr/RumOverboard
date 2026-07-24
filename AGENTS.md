# RumOverboard — agent rules

## Graphify-first navigation

For any codebase navigation task in this repository:

1. If `graphify-out/graph.json` exists, start with:
   - `graphify query "<question>"`
2. For relationship tracing between concepts, use:
   - `graphify path "<A>" "<B>"`
3. For focused concept explanation, use:
   - `graphify explain "<concept>"`
4. After code changes, refresh graph state:
   - `graphify update .`
   - If no graph exists yet: `graphify extract . --code-only`

## Notes

- Treat Graphify as the default navigation layer before broad raw file scanning.
- Keep generated graph artifacts (`graphify-out/`, `graph.json`) out of commits.

## Mechanics development workflow

For every new ship mechanic, follow this order:

1. Build the mechanic in isolation first.
2. Create a dedicated test scene with required conditions (water on/off, focused actors only).
3. Add an in-scene debug menu/window with runtime controls, reset, and save-to-config.
4. Bind all cross-component dependencies through a ship-level aggregator (or a small set of aggregators), not ad-hoc links.
5. Keep each mechanic's code in its own feature directory (scene controller, debug UI, config, runtime systems together).
6. Only after the isolated scene is stable, integrate the mechanic into the gameplay ship.

