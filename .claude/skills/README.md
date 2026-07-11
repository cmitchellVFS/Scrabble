# coherence Skills

Symptom-triggered guidance skills for AI agents working in coherence-powered Unity projects. Each skill describes a class of networking problem and the idiomatic resolution patterns.

These are intended to be installed at `~/.claude/skills/<name>/` (user-global) or `.claude/skills/<name>/` (project-local) so they auto-surface when a developer describes a matching symptom.

| Skill | Triggers on |
|---|---|
| [coherence-bandwidth-at-scale](coherence-bandwidth-at-scale/SKILL.md) | "Too many networked entities," lag with crowds/swarms/bullet hells, large-world bandwidth |
| [coherence-authority-handoff](coherence-authority-handoff/SKILL.md) | Shared/contested objects, pickups, seats, "two players grabbed at once" |
| [coherence-persistence](coherence-persistence/SKILL.md) | Persistent rooms, join-in-progress catch-up, save/load with networked entities, growth/decay timers |
| [coherence-animation-networking](coherence-animation-networking/SKILL.md) | Animation triggers not replicating, late joiners stuck in T-pose, hitting the 32-field-per-component schema limit |
| [coherence-rpc-routing](coherence-rpc-routing/SKILL.md) | Choosing message targets, ordered vs unordered, request/response, big payloads |
| [coherence-retrofit](coherence-retrofit/SKILL.md) | Adding multiplayer to an existing single-player game, dual-mode codebases |
| [coherence-baking](coherence-baking/SKILL.md) | Driving coherence's bake / update-bindings / baked-folder recovery from an AI agent via Unity MCP |
| [coherence-sync-field-ordering](coherence-sync-field-ordering/SKILL.md) | A `[Sync]` field whose value depends on another synced field; derived values wrong on remotes; cross-field logic in a setter |
| [coherence-nested-prefabs](coherence-nested-prefabs/SKILL.md) | One CoherenceSync prefab referencing another CoherenceSync prefab (nested at edit time); synchronizing nested entities with PrefabSyncGroup |

## Installation

Either:

- Copy a skill's directory to `~/.claude/skills/<skill-name>/` for global availability.
- Or copy to `<your-project>/.claude/skills/<skill-name>/` for project-scoped availability.

The frontmatter `description` field is what triggers the skill — the agent auto-loads the skill when the user's request matches that description.

## Skill conventions

Each `SKILL.md` follows:

1. Frontmatter (`name`, `description`, `metadata`).
2. **When to use this skill** — symptom-led, one-line scenarios.
3. **The core idea** — one paragraph framing.
4. **Patterns** — concrete C# snippets with the *minimal* code needed.
5. **When it works / when it doesn't** — explicit rules.
6. **Gotchas** — known footguns and how to avoid them.
7. **See also** — links to nugget behaviours that implement the pattern.

Code snippets are generic — no project-specific names.
