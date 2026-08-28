# Croquet — mobile game

2D top-down croquet for iOS and Android. Unity 2022.3 LTS for presentation,
with all rules and physics in an engine-free C# core.

## Commands

```sh
dotnet test                 # the whole core suite, ~0.1s, no editor needed
dotnet test --filter Roquet # one test or one class
dotnet build
```

`dotnet test` is the loop. Run it after every change to `core/`. It needs no
Unity, no device, and no graphics.

## Layout

| | |
|---|---|
| `core/` | rules + physics. **No `UnityEngine` reference, ever.** netstandard2.1 so Unity can consume it |
| `tests/` | xunit against `core/`. net9.0 |
| `unity/` | the Unity project — rendering, input, UI, audio. Not created yet |

## The two rules that matter

**1. The core never references the engine.** If `core/` ever needs a Unity
type, the design is wrong — pass a plain struct instead. This is what keeps the
test suite instant and lets an AI play thousands of candidate shots per second
without rendering anything.

**2. The simulation is deterministic.** Same input, same output, on every
device. This is not a nicety: online play is planned to send *shots* (angle,
power, contact point) rather than ball positions, which only works if both ends
land in the same place. Concretely, inside the sim step:

- doubles only — no floats, no `decimal`
- `+ - * /` and `Math.Sqrt` only. **No `Sin`, `Cos`, `Atan2`, `Pow`, `Exp`** —
  those are not guaranteed identical across platforms or runtime versions
- no `float.Parse`, no culture-dependent anything, no iteration over a `Dictionary`
- no wall-clock time, no `Random` without an explicit seed carried in the state

Trigonometry is fine in *aiming* code: that runs before the shot and its output
is an input to the sim, not part of it.

`SimTests.The_same_shot_replays_bit_for_bit` guards this with exact equality.
If it ever goes red, something non-deterministic got into the step — fix that
rather than loosening the assertion to a tolerance.

## Feel

Every constant that affects how the game feels lives in `CourtSpec`:
friction, restitution, ball radius, court size, sleep speed. Nothing is
hard-coded in `Sim`.

Feel is tuned by hand, repeatedly, from real play. **Tests must build their own
`CourtSpec`** rather than relying on the defaults, so a tuning pass never turns
the suite red. Assert things that hold for any feel — energy leaves the system,
balls never overlap, a shot replays identically — not "the ball stops at 12.4
metres".

## Current state

Physics: rolling with friction, ball-to-ball contact with restitution,
substepping against tunnelling, boundary detection.

Not built yet: hoops and the peg, the rules/turn state machine, shot input,
the Unity project, AI, UI.

The nine-wicket course and deadness rules are already worked out in the
scorekeeper app at `../Croquet Score App/index.html` — `COURSE` and the
deadness handling there are the spec to port.
