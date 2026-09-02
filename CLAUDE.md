# Croquet — mobile game

2D top-down croquet for iOS and Android. Unity 2022.3 LTS for presentation,
with all rules and physics in an engine-free C# core.

## Commands

```sh
dotnet test                        # the whole core suite, ~0.1s, no editor needed
dotnet test --filter Roquet        # one test or one class

play croquet                       # the playable lab, opens localhost:5055
./play.ps1 --no-open               # the same, without a browser
```

`play croquet` works from any directory. It is a `play` function in the user's
PowerShell profile that does nothing but call `play.ps1` here, so this repo
stays the source of truth for how the lab is launched and the profile never
needs touching again.

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
| `play.ps1` | launches the lab; what `play croquet` calls |
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

## Two games

| | Nine wicket | Association |
|---|---|---|
| Rulebook | `2020_Complete__9_Wicket__Rules.pdf` (USCA) | `Laws-7th-Edition-master-new.pdf` (WCF) |
| Court | 100 × 50 ft | 28 × 35 yd, **laid out here rotated** so its long axis is x |
| Hoops | 9, carrying 14 points | 6, each run twice = 12 points |
| Pegs | 2 (turning, finishing) | 1 |
| Points a ball | 16 | 13 |
| Continuation for hoops | one each, max 2 | **exactly one**, however many run |
| Hoop then a ball | contact ignored | **both count** |
| Taking croquet | four ways | croquet shot only |
| Out of bounds | option: any ball ends the turn | striker's own ball ends it |

`Variant` picks between them; `Field.For(v)` and `Field.CourtFor(v)` build the
court, and `Laws.For(v, options)` carries the differences. Everything else —
physics, events, the turn machinery, aiming — is shared.

**Not modelled in association play**: bisques, lifts and wiring, cannons,
baulk-line choice (balls come on at one fixed spot), playing either ball of the
side, and the yard-line subtleties of Laws 14–15 beyond replacing a ball a yard
in. Law 21.2's qualification about where the other ball stood relative to the
jaws is also skipped — any contact after a hoop counts as a roquet.

## The rules

`2020_Complete__9_Wicket__Rules.pdf` in the repo root is the USCA official
rules and is the authority. Basic rules only — none of the Challenging Options
are in force. When a rule question comes up, read the PDF rather than
reasoning from what croquet "should" do; several of these are counter-intuitive
and the first implementation got four of them wrong.

The ones that bite:

- **Bonus shots**: one for a wicket or the turning stake, two for a roquet.
  **Never three**, and they **do not accumulate** — earning any forfeits what
  was owed, so a bonus shot that scores a wicket leaves *one* shot, not two.
- **Order inside a stroke decides everything.** Wicket then a ball: the wicket
  counts and the contact is ignored. Ball then a wicket: two shots for the
  roquet and *the wicket does not count at all*.
- **Deadness lapses at the start of your next turn**, or when you clear your
  next wicket, whichever comes first. Carry-over deadness is Option 1 and is
  not in force. Hitting a dead ball costs nothing; it just earns nothing.
- **Out of bounds carries no penalty.** The ball is replaced one mallet length
  (36 in) in, *perpendicular* to the line it crossed, and play continues.
- **A ball driven through its own wicket by someone else scores the point** for
  its side — but earns nobody a bonus shot.
- The first bonus shot after a roquet may be taken **four** ways: mallet head,
  foot shot, croquet shot, or from where it lies. The second is always an
  ordinary continuation.

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

## The bot

`Bot` plays candidate strokes on **clones of the real Game** and reads the real
`StrokeResult` back. That is deliberate: an evaluator with its own copy of the
rules would drift from them silently, and this way the bot can never believe
something the game would not do.

Candidates are generated the way a player thinks — at each live ball, at the
hoop in order, at a spot in front of it — and only then topped up with a coarse
sweep. Blind sampling spends almost all its budget on angles that hit nothing.
Power is sampled as a **distance to roll** and converted with `v² = 2ad`, so
"reach that ball" is expressible directly.

`Bot.Fast()` / `new Bot()` / `Bot.Strong()`. Normal is about 190 strokes
searched in ~110 ms. `Lookahead` costs several times the budget per ply and is
off by default — the evaluator already rewards the position a stroke leaves, so
one ply plays a recognisable break.

**It is currently very strong** — it has taken a ball all the way round in a
single turn. Making it beatable is a separate job from making it good; the
obvious lever is aim error scaled by a difficulty setting, which does not exist
yet.

## Current state

Done and tested: rolling, contact, hoops and pegs as obstacles, the nine-wicket
layout, running hoops in the right direction, the turn and bonus-shot machinery
above, all four bonus ways, deadness, out-of-bounds replacement, points scored
for balls driven by others, staking out, sides and winning.

Not built yet: the Unity project, AI, shot preview, rovers and poison,
"wicketed" balls, and the rule that a ball resting within a mallet length of
the boundary is brought in.

The scorekeeper app at `../Croquet Score App/index.html` was the original spec
for the course; `Course.Labels` here mirrors its `COURSE`.
