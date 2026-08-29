# Croquet — mobile game

2D top-down croquet for iOS and Android. Unity 2022.3 LTS for presentation,
with all rules and physics in an engine-free C# core.

## Commands

```sh
dotnet test                        # the whole core suite, ~0.1s, no editor needed
dotnet test --filter Roquet        # one test or one class
dotnet run --project tools/Croquet.Lab    # the playable lab, opens localhost:5055
```

`dotnet test` is the correctness loop — run it after every change to `core/`.
It needs no Unity, no device, no graphics.

`Croquet.Lab` is the *feel* loop, and the two are not the same job: the tests
say the physics is right, the lab says whether it is any fun. It is a tiny
web host that runs the real `Croquet.Core` and hands whole shots to a browser
canvas as frames. Drag to aim, and there are live sliders for friction,
restitution and power, so feel can be found by hand and reported back as
numbers. Pass `--no-open` to skip launching a browser.

Simulating the entire shot on the strike and animating the result is not a
shortcut for the lab's benefit — it is how the game itself will work, because
a deterministic sim knows the outcome the moment the ball is struck. Same
shape for online play and for the AI's search.

## Layout

| | |
|---|---|
| `core/` | rules + physics. **No `UnityEngine` reference, ever.** netstandard2.1 so Unity can consume it |
| `tests/` | xunit against `core/`. net9.0 |
| `tools/Croquet.Lab/` | dev-only web host for tuning feel. Never ships |
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

## How a shot resolves

Worth reading once, because the layering is the whole design.

`Sim` knows nothing about croquet. It rolls balls, bounces them off each
other and off hoop uprights and pegs, and records an ordered list of
`ShotEvent`s — contacts, hoop crossings, peg hits, going out — each stamped
with the substep it happened on.

`Game` never looks at a velocity. It reads those events and applies the rules.
**Order is why the events are stamped**: running a wicket clears your deadness,
so a ball you were dead on is a live roquet if the wicket came first in the
same stroke and nothing at all if it did not. Final positions cannot tell
those two apart.

Whether a hoop was *run* is likewise not a question about final position. A
ball can pass through and roll back out, or arrive on the far side round the
outside. So crossings are counted signed, only when they pass between the
uprights, and the net is read at the end of the shot.

The turn: a roquet owes a croquet stroke **and** a continuation; a point owes a
continuation; earn nothing and the turn passes. Going out, pegging out, or
sending the croqueted ball off the lawn ends it regardless.

## Current state

Done and tested: rolling, contact, hoops and pegs as obstacles, the
nine-wicket layout, running hoops in the right direction, the full turn state
machine, deadness, pegging out, sides and winning.

Not built yet: the Unity project, AI, shot preview, out-of-bounds replacement
(a ball currently stops on the line rather than coming back in a foot), and
the finer croquet-stroke placements — the lab always places the striker on the
side it approached from.

The scorekeeper app at `../Croquet Score App/index.html` was the spec for the
course and deadness; `Course.Labels` here mirrors its `COURSE`.
