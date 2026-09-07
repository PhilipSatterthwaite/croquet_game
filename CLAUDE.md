# Croquet — mobile game

2D top-down croquet for iOS and Android. Unity 6 (6000.6.0f1) with URP 2D for
presentation, with all rules and physics in an engine-free C# core.

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
| `tools/Croquet.Train/` | dev-only. Learns the bot's judgement by self-play. Never ships |
| `weights/` | what training produced. Text, readable, meant to be argued with |
| `play.ps1` | launches the lab; what `play croquet` calls |
| `unity/` | the Unity project — rendering, input, UI, audio |

`core/` is not copied into `unity/`. It carries a `package.json` and an
`asmdef`, and `unity/Packages/manifest.json` references it as a local package
(`file:../../core`), so the editor compiles the same sources the tests run
against — never a DLL somebody remembered to rebuild. `Directory.Build.props`
redirects build output to `build/` at the repo root to keep `core/` nothing but
source, because Unity compiles every `.cs` it finds under a package and the
generated ones in `obj/` would collide.

The asmdef sets `noEngineReferences`, so rule 1 below is **enforced by the
compiler**: if anything in `core/` reaches for `UnityEngine`, the editor fails
to build rather than quietly making the simulation un-testable.

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

**Strength is a distance, never a speed.** The bot samples power as a distance
to roll and converts it with `v² = 2ad` (`Bot.Speed`), and the player's pull
does exactly the same (`AimControl.Roll`). Both then mean the thing a player
actually judges — "reach that ball", "get in front of the hoop".

The pull is **not** linear in that distance. `AimControl.rollCurve` raises the
drag to a power (2 by default), so the first half of the pull buys the first
quarter of `maxRoll` — a hoop-to-hoop touch is a slow, controllable draw, and
the long shots share the top of the bar, where nobody is being precise anyway.
That curve is what the power bar's ticks are divisions of.

Worth knowing, because it looks like a reversal: an early version had that
exact curve by accident, by mapping the pull to *speed* and letting `v² = 2ad`
square it. Tearing it out was right at the time and the diagnosis was wrong.
What made it unplayable was the ceiling — a full pull rolled 41 m on a 30 m
court, so everything reachable lived in the bottom quarter of the drag. With
`maxRoll` set to a court's length the same curve is all playable. Keep the
curve, keep it explicit, and keep `maxRoll` honest.

The machine's aim inverts it (`AimControl.Address`) so its drawn-back pull
means the same distance a person's would. Change one and the other must move.

Friction is really a setting for **how long a stroke takes**, which is the
other half of how a shot reads. Under constant deceleration a ball struck to
roll `d` metres stops after `√(2d/a)` seconds, so the length of the court at
0.6 is a ten-second glide. The Unity game runs at 1.6, which puts a
full-stretch shot at about six seconds. `CourtSpec`'s own 0.6 is left alone —
it is what the tests build against, and tuning it would move the suite.

`ReplayTests.A_stroke_rolls_the_distance_it_was_struck_for` holds both halves:
that a stroke struck for `d` travels `d`, and that the frames take `v/a`
seconds to play it. Both are feel-independent — they hold at any friction — so
tuning never turns them red.

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

`Replay` is the layer above both, and it is what every front end calls. It
plays the stroke for real, lets `Game` apply the rules, and then rebuilds the
same shot on a scratch world as frames for the eye. Nothing animates its way
towards an answer — the answer exists the moment the ball is struck, and the
animation is a rendering of it.

It steps the frames at **the same dt `Sim.Settle` uses** and samples every
second one. That is not a detail: `Sim` subdivides each step according to the
fastest ball, so a coarser step integrates a different sequence and finishes
somewhere else — the ball would end the animation a few centimetres from where
the rules had already put it and visibly jump on the last frame.
`ReplayTests.The_last_frame_is_where_the_ball_actually_finished` holds that to
exact equality; if it ever needs a tolerance, fix the step rather than the
assertion.

## A match is its strokes

`Stroke` is one stroke as a single value — aim, power, and for a bonus the way
and the placement. About forty bytes, and the same forty whether the shot moved
one ball or six. `Match` is a `MatchSetup` plus the ordered list of them.

**The list is the game.** Because the simulation is deterministic, a setup plus
its strokes reproduces the position exactly, so that one type is the save file,
the replay, the spectator feed, and what a client that dropped out is handed to
catch up. Online play is then agreeing on a setup and appending to the same
list at both ends.

`Match.Play` is the only way a stroke enters a match, so the list cannot fall
out of step with the game it describes. `Stroke.Fits` is the shape check a
server makes before applying something a client sent — a bonus when one is
owed, an aim that points somewhere, a power that is a number — so a malformed
message is refused rather than throwing inside the rules.

`MatchSetup` carries the **court in full** rather than deriving it from the
variant. It is ten numbers, and the alternative means the day someone tunes the
friction, two clients on different builds play the same strokes on different
lawns and drift apart with nothing to point at.

### Telling the two ends apart

`Checksum` reduces a game to one number, and `Match` records it after every
stroke. Without it a desync does not stop anything: both ends carry on happily
looking at different lawns, and it surfaces several turns later as an argument
about whether a ball was hit. With it, it is two numbers differing on the
stroke it happened.

It hashes raw IEEE bits, not rounded values, because a divergence starts in the
last bit and grows — a tolerance would hide the only case worth knowing about.
Deadness is sorted before hashing: a `HashSet` has no order, and two clients
that agree about the game must not disagree about the number.

### Whether the platforms actually agree

`Reference` is one fixed match with a pinned hash, and it is in `core/` so that
every runtime can play the *same* one. **Measured: CoreCLR (`dotnet test`) and
Unity's Mono both land on `0x91CC2FA93F2FA2DC`** across eight strokes including
collisions, hoop crossings and a ball put off the lawn.

IL2CPP on a device is still unchecked — `Croquet ▸ Check determinism` reports
it, and `DeterminismCheck.Report()` is written to run inside a player build too.

If that number ever moves, it is not automatically a bug: any deliberate change
to the simulation moves it, and the answer is to re-pin. What it must never do
is move on its own, or differ between two machines running the same build.

`Game` never looks at a velocity. It reads those events and applies the rules.
**Order is why the events are stamped**: running a wicket clears your deadness,
so a ball you were dead on is a live roquet if the wicket came first in the
same stroke and nothing at all if it did not. Final positions cannot tell
those two apart.

### A hoop is a gap with thickness

Whether a hoop was *run* is not a question about final position. A ball can
pass through and roll back out, or arrive on the far side round the outside.

And it is not a question about a plane either, which is the mistake this made
first. A hoop has two faces and a ball can **stop between them** — that is the
USCA diagram's ball C, which has NOT scored. So `World.Side` tracks which side
of each hoop every ball is on: `-1`, `+1`, or unchanged while it is in the
jaws. A side only changes when the ball is entirely past a face.

Everything the rulebook says then falls out of that one decision:

- a ball left in the jaws scores nothing, and scores on the stroke that carries
  it out — so the side state **persists between strokes**, and `Game.Clone` has
  to copy it or a bot would never find the stroke that finishes the run;
- "if a ball passes through a wicket but rolls back, it has not scored" — the
  crossing tally and a final clearance check, both required;
- "if a ball travels backwards through its wicket to get position, it must be
  clear of the non-playing side to then score the wicket in the correct
  direction" — half a retreat leaves the side unchanged, so nothing is scored.

**Leaving the mouth forgets the side**, and that is what keeps this a hoop
rather than an infinite plane ruled across the court. A side that survived the
ball wandering off is a latch anything can flip from either end: seen on the
near side once, seen on the far side later, crossing recorded — with no
requirement that the ball was ever between the wires in between. So a ball
outside the uprights is at no hoop at all, and the next sighting in the mouth
is a first sighting. That is the rulebook's ball arriving "on the far side
round the outside", which has scored nothing.

`WicketTests` holds all of it. `CroquetGame.auditScoring` holds the same thing
from the other end, at runtime: it reads the frames the eye actually saw and
warns in the console if a point was awarded to a ball that crossed the hoop's
line wide of the gap. Four full bot games under `dotnet test` scored 145 hoop
points without it tripping, so if it ever fires the fault is above the rules
and not inside them.

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

`Lookahead` costs several times the budget per ply and is off below Expert —
the evaluator already rewards the position a stroke leaves, so one ply plays a
recognisable break. A stroke is chosen in roughly 100 ms.

### Difficulty is a hand, not a smaller search

**Three levels**: `Bot.Beginner()` / `Casual()` / `Expert()`, each taking an
optional seed (they are deterministic, so a test wanting several independent
attempts must vary it). A weaker level is mostly **not** one that
searches less: a bot that searches less still picks a sensible shot and plays it
perfectly, which reads as an unbeatable player with poor ideas. Weakness is
mainly `AimError` and `PowerError` — its hand shakes, so it misses, like a
person.

The beginner is the exception, and deliberately: it gets `FreePlies = 0` and
fewer `RiskChecks`, so it cannot see what a roquet is for and barely checks
whether a shot is one it can play. That is weakness in the HEAD, and it reads
as a different player from one who merely misses — which is what was wanted,
because a beginner who plays expert croquet badly is not a beginner. Its hand
close in is now steady: it runs a hoop it is standing in front of every time at
0.6 m, and 95% of the time at 1.0 m. Difficulty belongs on the shots that are
genuinely hard.

There were four — beginner, casual, steady, expert — and they were all too
accurate. Three now, every one of them a worse hand than the level it replaced:

| | beginner | casual | expert |
|---|---|---|---|
| strokes to go round ×3, before | 90 | 81 | 39 |
| …after | 108 | 81 | 60 |
| hits a ball at 3 m | 35% | 90% | 100% |
| …at 6 m | 11% | 35% | 91% |
| …at 10 m | 4% | 12% | 50% |
| …at 15 m | — | — | 20% |

Casual's stroke count barely moved even though its hand got half again worse,
and that is the thing to understand about this metric: at that level the
*search* is what costs the strokes, not the hand. Strokes-to-go-round measures
both together and is dominated by whichever is weaker. So `BotLadderTests`
measures the hand on its own — `Bot.Wobble` applied to a stroke that is already
aimed, no search involved — and holds two things: each rung is worse than the
one above it at every range, and the rungs are at least fifteen points apart at
six metres. Six metres is where a level shows; two levels that agree there are
one level with two names.

`Spread` sets how far off a stroke goes as a multiple of that base error, and
grows with the **range** of the shot — **linearly and quadratically**, which
matters more than it sounds. With only a linear term the two ends of a level's
game are tied together: turning its long game down drags its short game down
with it, and a beginner ends up missing taps in order to keep missing roquets.
Nobody's error at a metre is a fifth of their error at five; it is nearer
nothing. The square separates them — negligible close in, dominant by the time
a shot crosses the court. That is where a person's uncertainty
actually lives: nobody misjudges a tap and everybody misjudges a shot across the
court. It matters that the floor is small (0.12), so anything within comfortable
range goes in whoever is playing and the levels separate on the long shots.
`Gauss` is clamped at 2.5σ and the power multiplier floored at ⅔, because the
error is multiplicative and an unclamped tail crosses zero — that produced a bot
striking at three per cent of its intended power and dribbling the ball.

### Judgement is learned, striking is not

`Bot.AimError` and friends are how well it strikes the ball. `BotWeights` is
what it is *trying to achieve* — and those are separate things, tuned in
separate ways. The hand is set by hand, against the width of a hoop. The
judgement is **measured, by self-play**.

`tools/Croquet.Train` holds a champion set of weights, makes a few mutants each
round by nudging every weight, plays each mutant against the champion, and
keeps whichever wins. That is an evolution strategy, and it is the right family
here for one reason: there is no derivative of "won the game" with respect to
"what a roquet is worth", so nothing gradient-based applies and what is left is
to try changes and keep what works.

```sh
dotnet run --project tools/Croquet.Train -c Release -- time
dotnet run --project tools/Croquet.Train -c Release -- train --rounds 40 --games 120
dotnet run --project tools/Croquet.Train -c Release -- match weights/trained.txt --games 400
```

**A run of this length will be interrupted, so being interrupted is a normal way
for it to end.** Ctrl+C stops at the end of the current series, writes the
champion out, prints what it learned, and skips the closing baseline match —
nobody who has just pressed Ctrl+C is waiting three more minutes to be told a
number, so it prints the `match` command instead. The champion is written the
moment it is accepted, not at the end, so even a killed process leaves the last
improvement on disk. Cancellation is checked once per stroke rather than once
per game, which is the difference between Ctrl+C working and Ctrl+C appearing
not to.

It reports a live line per series with a bar and a running estimate of the time
left. A round is minutes long and a run is hours, so a tool that prints nothing
until a round ends is indistinguishable from one that has hung — and the only
thing to do about a run you cannot tell is working is kill it.

**Fitness is games won and nothing else.** Not evaluator score, which is
circular — both sides believe their own evaluator. Not points scored, because a
set of weights that piles up points and loses is worse than one that wins ugly.
Both sides always get the same hand from the same seed, so a match measures
judgement rather than striking; every seed is played twice with the sides
swapped, so a lucky opening cannot favour anyone. Identical weights score
exactly 50%, which is the check that the whole apparatus is fair.

Three things it would go wrong without, all of them learned the hard way here:

- **The scale is pinned** (`BotWeights.Normalised`). Only ratios between the
  weights mean anything, so the overall size is a free parameter — and a free
  parameter is one a search wanders along. Worse: the terminal win bonus is
  fixed and enormous, so shrinking everything else is a way to make winning
  relatively louder, and the cheapest route to that is a bot that values
  nothing at all until it can win on the stroke in front of it. The first run
  did exactly this, dropping every weight by about 60%.
- **The winner is re-played before it is believed.** Taking the best of six
  noisy scores finds the luckiest challenger, not the best one — six coins
  flipped sixty times will hand you one at 58% every round, for ever, on no
  merit. So the round's winner faces fresh seeds and has to win twice.
- **The run ends by playing the baseline.** A chain of small wins over the
  previous champion can still drift somewhere worse than it started.

Metres are not points: `inFrontWidth` is 0.9 because that is a distance on a
lawn, so `Normalised` leaves the distances alone and rescales only the values.

Weights are text and belong in `weights/`. Nothing loads them at runtime —
`core/` does no file IO, which is what keeps it consumable by Unity under
IL2CPP — so shipping a trained set means pasting it into `BotWeights.Default`
once it has decisively beaten what is there. `BotWeights.HandTuned` keeps the
original guesses as a fixed point, because once a learned set becomes the
default, measuring against the default asks whether it beats itself.

**The first run, adopted:** 34,000 games, then **73.5% over 400 games** against
the hand-tuned original (95% confidence: 69.0–77.6%). It says position is worth
far more than guessed — standing in front of your hoop scores 1127 against a
hoop's 1200 — that letting an opponent score is 2.4× worse than scoring
yourself is good, and that losing the turn was double-counted, being the one
weight that went down.

**It declined roquets, and that turned out to be the search's fault, not the
weights'.** Two things were missing, and both are fixed:

- **The croquet shot was not a candidate.** The generator aimed at the
  striker's own hoop or straight through the ball, from placements swept
  blindly round the circle. But a croquet stroke is chosen in two independent
  halves — the placement decides where the OTHER ball goes, because it leaves
  along the line of centres, and the aim off that line decides how far it goes
  and where yours ends up. `Bot.Splits` generates that properly now: place so
  the line of centres points somewhere worth sending the ball, then fan the aim
  either side of it.
- **A roquet was scored as though the turn had ended.** A roquet buys two
  strokes, and with `Lookahead = 0` the bot saw only itself standing next to a
  ball six metres from its hoop — worse than a quiet tap into position, and it
  chose the tap, correctly, on what it could see. `Bot.FreePlies` (2 by default)
  always looks into strokes already won, whatever Lookahead says, and the two
  candidates it deepens are the best overall AND the best that keeps the turn:
  deepening the top of one sorted list cannot work, because a roquet's shallow
  score is exactly the thing that is wrong about it.

That took the roquet in the failing test from 365 to 1199 against a tap's 365,
and it costs 2.7× the search time — about 19 ms a decision, comfortably inside
the budget. Free plies run COARSE (fewer sweep angles and placements), because
they answer one question — what is this roquet worth — and a cheap answer to it
beats a fine one costing four times as much.

**The shipped weights are therefore stale**, and knowingly so. `roquet = 640`
was learned in a world where a roquet really was nearly worthless, because the
bot could not convert one. They still beat the hand-tuned original 72.5% over
200 games under the new search, so they are not wrong — but retraining now that
roquets are worth something is the obvious next run.

### The value net, and why it lost every game

`core/Net.cs` and `core/Sight.cs` are a small value network -- 60 numbers in
describing a position, one out saying how often this side wins from it -- and
`tools/Croquet.Train` can `collect` self-play positions labelled by who won and
`learn` a net from them. It is **not in use**, and the reason is the most useful
thing here.

The first run fit well: 67.5% of held-back games called correctly, loss 0.582
against the 0.693 of always guessing the average, and almost no gap between
training and held-back, so no overfitting. Then it played the linear weights and
lost **300 games out of 300**.

Nought from three hundred is never "slightly worse judgement" -- a mediocre
evaluator still wins a third by luck. It is always a fault, and there were two:

- **A reward and a value are not the same thing, and the search could only add
  up one of them.** `Evaluate` is a REWARD -- how much good this stroke did --
  so a stroke plus its discounted follow-up is the total good. A net is a VALUE:
  it already contains everything that happens afterwards, so adding the
  follow-up counts the future twice and breaks the bounds with it. A stroke that
  kept the turn could reach 1.75; a stroke that WON returned 1.0 and was never
  deepened, the game being over. Anything above 0.25 outranked winning, and the
  bot could not close out a game. Fixed: with a net the deeper estimate replaces
  the shallow one rather than adding to it.
- **It was never able to choose a stroke, only to read a scoreboard.** Measured
  by `NetTests`: across 144 wildly different strokes from one position, the net
  returned values from 0.477 to 0.547 -- a spread of 0.07, standard deviation
  0.008 -- while the weights spread the same strokes over 3,000 points. Forty
  times less separation, all of it hugging one half. The search was picking the
  largest of a set of numbers that were all the same, which is picking at
  random, and random play loses every game.

The second is the real lesson, and it is not a bug to be fixed by more games.
**Predicting a winner and choosing a stroke are different jobs.** Within one
turn every candidate leaves nearly the same position -- same points, same balls,
a metre here or there -- so a net that keys on who is ahead scores them all
alike and is perfectly accurate about a question nobody asked. It did not help
that `Sight` hands it `mine`, `theirs` and `mine - theirs` outright: that is the
easiest possible signal for predicting a winner and it is IDENTICAL across every
candidate in a turn. The model optimised the objective it was given, which was
the wrong one.

`NetTests.How_much_does_the_net_separate_strokes` is the gate for the next
attempt: any net at `weights/net.txt` must spread those 144 strokes by more than
0.15 or it cannot be used to choose between them, whatever its accuracy. The
first attempt is kept at `weights/net-first-attempt.txt` rather than deleted,
because a 67.5% predictor that plays at zero is worth being able to re-examine.

### The search knows whose hand it is

Every candidate is first played **perfectly**, which makes a thirty-metre roquet
look certain and better than any quiet positioning shot. So the leaders are then
re-priced by `Expected`: replayed a few times with the error actually on them,
scoring the average outcome rather than the best case. A shot that only works
when struck perfectly collapses on its own, with no rule anywhere about long
shots being bad.

Two things make that bite, and both were bugs when absent:

- The re-priced candidates are the **only** ones that may then be chosen. A
  best-case score and an expected score are different quantities; sorting them
  together just hands the choice to whichever heave was ranked eleventh.
- The shortlist skips **near-duplicates**. Straight off the top the leaders are
  one shot at a dozen strengths, so re-pricing them only finds the least-bad
  version of that shot and the alternative never competes.

`BotTests.The_levels_are_actually_in_order_of_strength` holds the ladder end to
end (108 / 81 / 60 strokes to get round three times) and `BotLadderTests` holds
the hand that produces it. `BotQualityTests` guards the behaviours a person
actually notices — that it does not dribble the ball, does not heave it across
the court, and that every level runs a hoop it is sitting in front of. That
last one is the floor under all of this: however bad a hand gets, a ball square
in front of its hoop at a metre goes through, because nobody of any standard
misses that and difficulty belongs on the shots that are genuinely hard.

## The Unity game

The scene is `unity/Assets/Scenes/SampleScene.unity`: a camera carrying
`CourtCamera`, a global 2D light, and one object, `Croquet`, carrying the four
components that are the game.

| | |
|---|---|
| `Shapes` | every sprite, built in code. No art to import, nothing to wire |
| `Ui` | the palette and the pieces every screen is built from |
| `CourtView` | the lawn, boundary, hoops and pegs, from the real `Field` |
| `CroquetGame` | owns the `Game`, drives the turn, animates a `Replay` |
| `AimControl` | the ring, the swing and the guide; the hand and the machine's |
| `AimGuide` | where a stroke goes and what the collision does. Exact |
| `GameMenu` | the screen before the game, and the only place settings live |
| `GameHud` | the power bar and who is dead on whom. Nothing else |
| `CourtCamera` | zoom, and following the ball that is actually moving |
| `Editor/GameViewAspect` | snaps the editor's Game view to 16:9 |

**The game is landscape.** The court is 2:1 and there is no sensible portrait
presentation of it, so the player settings allow the two landscape orientations
and nothing else. On a portrait view the camera has to fit that lawn by its
width, which leaves the whole game in a band across the middle with empty
surround above and below.

The editor's Game view is a separate thing from the player settings and has no
public API — `GameViewSizes`, `GameViewSizeGroup` and `GameView` are all
internal — so the `Croquet` menu sets it by reflection, wrapped so a Unity
version that has moved those names reports it rather than throwing.

Prefer **`Game view — 1920 x 1080`** over the aspect-ratio entry. An aspect
ratio renders at whatever size the Game view window happens to be, so a small
window is a small render blown up, and the whole game looks low-resolution for
a reason that is nothing to do with the game. A fixed size always renders at
that size and is scaled down to fit — which is also what a device does.

Nothing in any of them decides anything about croquet. `Croquet.Core` plays the
stroke and applies the rules; these drive the loop and draw what came back.
Every position on screen came out of the simulation.

The machine searches on a **worker thread**, against a `Game.Clone()`, so a
hundred milliseconds of thinking is not six dropped frames and the main thread
can go on drawing the game the search is not touching.

### Two scenes

`Menu.unity` is the start screen and `Game.unity` is the court, and they are
separate scenes rather than one scene with a panel over it. Everything decided
in the menu is decided once, before there is a game to decide it about, so
pressing Play *loads* the court — the menu is not something the game has to
keep hidden and remember to put back, it is not there.

`MatchSettings` is the handful of statics that survive the load between them.

In the game, `PauseMenu` offers exactly two things: Resume, and end the game
and go back to the menu. Escape opens and closes it. It sets the turn aside —
the machine's search stops — rather than freezing time.

**Physics is not in either menu.** How the lawn plays is what the game IS: the
same everywhere, the same for everybody, and no more a per-match choice than
the bounce of a tennis ball. Friction and the length of a full stroke live on
`CroquetGame` where they can be tuned in the inspector and then left alone.

`GameHud` keeps almost nothing. It narrated the turn in a paragraph top-left
and reported every stroke along the bottom, and both were reading out things
the lawn already shows — the ball is right there, with a ring round it and a
marker on the hoop it is for.

What is left is what the lawn genuinely cannot show:

- **The power bar**, always in the same corner, and with no words in it at all.
  A gauge that moves about is one you have to find before you can read it. It
  takes the striker's colour, so it says whose stroke it is without spending a
  word. It is marked off in ten divisions of the *pull*, rising from the bottom
  edge like a ruler with the midpoint running the whole way up, and they flip
  from light to dark as the fill passes them — a white scale vanishes under a
  white ball's bar exactly when the low divisions are being read.

  It carried the roll in metres, then the striker's name, "thinking" and "pull
  to strike", and every one of them went. A distance in metres is not something
  anyone can judge against a lawn, and the rest were saying what the bar itself
  already said. "Four ticks" is what a repeatable stroke is remembered as, and
  a word in the middle of a scale you are reading is just in the way.

  It is **long, shallow, and barely rounded** — a much tighter corner than a
  card gets, on both the bar and the fill inside it. A gauge is read from its
  ends and from where the fill has got to, and a generous radius eats the last
  few per cent at each end and rounds the fill's leading edge into something
  with no single place where the value is. Height was left over from when it
  held a word; none of the reading happens vertically.

  The bar is the instrument, which is why `AimControl.pullSpan` is short — a
  fifth of the screen, not a third. A long sweep is what you want when the
  gesture itself has to carry the precision; it does not, because the scale can
  be read while the thumb is still down.

  It shows **one** quantity: your own pull. It used to double as the machine's
  search progress, which swept the whole bar in about a tenth of a second and
  read as the corner of the screen flashing at the start of every turn. Two
  quantities sharing a gauge is one of them lying — a bar that is filling has
  to mean the stroke is getting harder — and how long the machine is taking is
  not something anyone waits to read off a scale.
- **The end of the game**, over the whole screen: the court behind a scrim, who
  won, how far round everybody got, and the way out. The rest of the HUD is
  switched off with it, because there is no stroke left to judge and nobody
  left to be dead on. As a card floating on the live lawn with the power bar
  and the deadness chart still up around it, the end of a game read as one more
  thing the game was telling you.
- **Deadness**, bottom right, as a little chart in colours rather than words.
  This is the one thing in croquet with no physical sign at all: two balls a
  foot apart look identical whether hitting one is worth two strokes or
  nothing. A row a ball, and in it a chip for each of the **other five** — a
  ball cannot roquet itself, and its own column was a permanently dark diagonal
  saying only that the chart knew which row it was on. Every slot keeps its
  place, so the chart has a shape that can be learned; a list that packs up and
  moves about has none.

  A live chip is **dark**, not a faint version of its colour. Dimmed colour
  gave six muted discs against six bright ones and had to be read rather than
  glanced at; against near-black, colour means exactly one thing. It arrives
  with a brief swell — `localScale`, so neighbours do not shuffle along — since
  a deadness picked up while you were watching the ball is otherwise silent.

Buttons are rounded rectangles, not pills, and rows do not force-expand their
children. `Ui.Row(expand: false)` with a `Ui.Filler` is how a single digit
avoids sitting in the middle of a slab a third of the panel wide — which is
what made everything look horizontally stretched however square the corners
were.

**The lawn is behind the whole interface**, so a press on a button is also a
press on the grass under it unless something says otherwise. `AimControl.OverUi`
is that something: `EventSystem.IsPointerOverGameObject()`, tested only at the
START of a gesture, so a drag begun on the grass and carried across a panel is
still that drag. Without it, pressing "Place it here" set the striker down and
in the same frame took the click as a fresh placement at the bottom of the
screen, throwing the ball to where the button had been.

Three things `Ui` knows that cost time to learn:

- **`ScreenMatchMode.Expand`, never match-by-height.** Matching on height scales
  a 1920-tall phone by 2.4×, and a card designed 560 wide comes out wider than
  the 1080-pixel screen it is on.
- **`flexibleHeight` defaults to "no opinion",** and a vertical layout then
  hands the spare space to whatever it likes — which turned the Play button
  into a green slab eight hundred pixels tall. `Ui.Tall` says zero.
- **An `EventSystem` has to exist** or nothing on a canvas can be clicked, and
  the failure looks exactly like buttons that do not work. `Ui.EnsureEventSystem`
  makes one with the Input System module, since the old backend is switched off.

### One light, and things that stand on the grass

Everything is lit from a single direction, `Shapes.Light`, over the player's
left shoulder, and from **low** — `Shapes.Elevation`. A court lit from two
directions reads as a collage; a court lit from overhead reads as flat, because
a ball lit from straight up is a bright disc with a thin rim and its shadow
hides underneath it. Raking light gives the ball a terminator and throws the
shadow somewhere it can be seen.

The trick worth knowing: a `SpriteRenderer` **multiplies** its colour through
the sprite, so `Shapes.Sphere` is a lit *white* ball and tinting it gives six
shaded coloured balls off one piece of artwork. The glint is a separate,
never-tinted sprite, because multiplying can only darken and a highlight has to
be brighter than the thing it sits on.

The ball carries the milling every real croquet ball has: fine grooves round
its middle, spaced evenly **in angle round the sphere** rather than evenly
across the picture, so they crowd toward the rim the way a real one's do when
you look down on it. The crown is left smooth. That foreshortening is most of
what makes it read as a sphere rather than a circle.

Sprites are baked at `Shapes.Res` (512). They are stretched to whatever the
zoom asks for and the aiming ring is drawn three metres wide, so at 128 the
whole game looked soft — it was being magnified rather than minified.

**A line needs a line sprite, not a white pixel.** `Shapes.Solid` stretched
into a thin rotated quad has hard edges with nothing anti-aliasing them, and a
diagonal aim line came out as a staircase of horizontal runs. `Shapes.Line`
carries an alpha ramp across its width, so the edge is soft in the texture
before it is ever turned, and the ramp scales with the thickness so it stays
about a pixel at any size. MSAA is on (4×) in `UniversalRP.asset` for the same
reason, and it covers every other rotated piece.

**Every sprite must be one unit by one unit**, because `Put` scales it to a
size in metres and a sprite has only one pixels-per-unit. The first `Shapes.Line`
was an 8×64 texture, which is a sprite one unit wide and *eight* tall; the fix
is a square texture with the ramp painted across it.

Each piece of furniture is three pieces: a soft `Shapes.Shadow` thrown away
from the light, the shaded body, and (on balls) the glint. The shadow being
*offset* rather than concentric is what says the ball sits on the grass instead
of being painted on it.

A ball has a fourth: the **seat**. Small, dark, barely offset, tucked under it.
With only a shadow thrown clear of it a ball reads as hanging above its own
shadow, which is exactly what "floating" looks like — and no directional shadow
can fix it, because where a ball meets a lawn the grass is in shade from every
direction at once. The two are doing different jobs: the cast shadow says where
the light is, the seat says the ball is touching something.

The other half of that is in the turf. `Shapes.Grass` carries a very fine, very
stretched octave — a low period across x and a high one across y — so the tile
has structure at nearly the scale of a ball rather than only broad mottling.
Something has to be visible *around* the ball at its own size for it to sit
down into anything.

`Layer` holds every sorting order in one place. Scattered across five files is
how a shadow ends up on top of the ball casting it, and the fix is never where
the problem appears to be.

Balls are `Shapes.Sphere`; hoop uprights are `Shapes.Cylinder` in white with a
glint over them; pegs are `Shapes.Wood`, which is the same cylinder with end
grain on it — looking down at a peg you are looking at the end of a piece of
wood, so what you see is growth rings and not the long grain you would see from
the side.

The lawn is drawn with `Shapes.Grass`, and every blade is a **lit tube that
casts its own shadow**. Forty-two hundred of them are laid into a seamless
512-pixel tile, each drawn twice.

It went through two wrong versions first, and the reasoning matters more than
the code. It was value noise, which gives an impression of grass and never one
blade. Then it was flat strokes, one brightness each — which is a *picture* of a
blade, and no quantity of them stops a lawn reading as paint.

The objection that fixed it: from directly overhead, what makes real turf look
three-dimensional is **not** that blades stand up. A vertical blade seen from
above is a dot. It is that they **lean**, so you see along their sides, and that
each one throws a shadow onto the turf beside it. Both of those are drawable
from a fixed overhead camera, and neither needs geometry.

So, per blade:

- **A shadow, sheared away from the light** by an amount growing from nothing at
  the root to its full height at the tip. That is what a shadow does, and it is
  why it fans out from the base instead of sitting under the blade like a
  sticker. All the shadows are laid down before any blade is, so a blade shades
  the turf under its neighbours and not only under itself.
- **A tube, shaded across its width** from a real surface normal — leaning out
  to the side at the edges, facing the sky along the middle — against
  `Shapes.Light` tipped up by `Shapes.Elevation` into an actual direction in
  space. Without that third component there is no such thing as a surface
  facing the sky, and every blade comes out lit the same whichever way it
  points. The cross-section is the whole difference between a stroke and a
  cylinder.
- **Topmost wins, never averaged.** A pixel takes the nearest blade covering it,
  not the mean of every blade touching it. Averaging is what makes a crowd of
  blades dissolve into a wash: two crossing blades come out as one mid-grey
  smear instead of one in front of the other.

Two things carried over from the noise and one had to go:

- Blades are dealt **around the mow direction** with a wide spread, rather than
  pointing anywhere. Grass that all points one way is a hairbrush; grass that
  points anywhere is a lawn nobody has cut.
- **The tile must not be squashed.** `grainStretch` was 2.6, which is how the
  noise got its direction. Blades are ruined by it: a squash flattens every one
  towards the horizontal, so a texture with a good spread of angles comes out as
  a single lawn-wide comb. It is 1 now, and the blades carry the mow themselves.
- `grain` went from 0.055 to 0.62 and the tint from white to a yellow-green.
  This is no longer a grey haze blended over green, it is grass with gaps in it,
  so turning it up adds blades rather than fog.

`grainScale` now sets **blade size** — at 1.6 m a tile a blade is three to five
centimetres. Turn it up and they become straw; down and they are fuzz again.
Thirteen thousand of them, short: a croquet lawn is mown, and a mown lawn is
dense and low, where long sparse blades read as a meadow nobody has touched.

**The lawn does not move, and that was tried both ways.** It had drifting bands
of gust shading over it, and then the blades themselves swayed -- two bakes
curved opposite ways, cross-faded, so they leaned rather than slid. Both worked,
in the sense of doing what they were built to do, and both were wrong: a croquet
lawn is a still thing to look at while you are judging a line across it, and
grass that breathes pulls the eye off the shot every second of the game. It also
cost two 512-pixel bakes at startup instead of one, for movement nobody wanted.

The blades still **curve**, in coherent patches driven by a coarse noise over
the tile, because that is the lie a lawn has after it has been rolled. It simply
does not change. Measured, frame to frame: 0.056 grey levels, against 4.3 when
it swayed.

**And it is still a picture on a plane.** `GrassField.cs` and
`Assets/Shaders/Blade.shader` build the same lawn as real geometry -- 277,000
blades, each a tapered ribbon standing out of the ground, verified in a running
scene with an 8 cm extent in z. They are not switched on, because **this project
renders through URP's 2D Renderer, which does not draw MeshRenderers at all**:
every patch reported `isVisible == false` from every angle, including a camera
parked directly on top of one, with the mesh, bounds, layer, culling mask and
shader all valid. Pointing the pipeline at the 3D renderer made 46 of them
visible immediately, which confirms the diagnosis and also shows the size of the
change -- switching renderers means re-checking every sprite, sorting layer and
2D light in the game. The tilt diagnostic on `GrassField` exists to settle that
question honestly whenever it is picked up: real blades stand when the camera
turns, painted ones smear.

Shading a post as a ball was wrong twice over — a post is a tube with a flat
top, and rounding it made the hoops read as beads resting on the lawn rather
than wire driven into it. What says "cylinder" is the edge, not the middle: a
flat top and a firm dark rim, with the height coming entirely from the shadow.
Keep that rim **thin**: a hoop post is a handful of pixels across, so a rim
starting at four fifths of the radius is a third of its area, which turned
white wire into grey dots once minified.

The lawn carries mown stripes one mower width across — six feet, alternating.
They double as a ruler: every light band is six feet, so a distance can be read
off the grass rather than guessed. **The grass runs past the court** on every
side and the court is marked out on it in white, because a lawn cut off exactly
at the boundary looks like a green card lying on a table.

### Which hoop is next, and not a moment sooner

Nine wickets look alike and the course doubles back on itself, so the lawn
cannot tell you which one you are for. `CroquetGame`'s target ring says where
it is: `Shapes.Dashes`, faint, breathing slowly and turning slowly. Neither
movement is loud on its own, and together they are what makes something that
dim findable at the edge of vision — movement is what the eye catches, so the
marker can spend its budget there instead of on brightness. A solid bright ring
was the loudest object on the court and it is only ever saying "this one".

**Read `ShownStriker` and `ShownPoint`, never the live `Game`.** A stroke
resolves the instant it is played: the rules run, the hoop is scored and the
striker is already playing for the next one — all before one frame of the ball
rolling has been drawn. An interface reading the Game directly announced the
result of a shot still in the air, with the marker jumping to the next wicket
as the ball left the mallet. `Replay.PointBefore` is what the shot was about,
and those two properties hold it for as long as the shot is on screen. The
stroke report is held back the same way, for the same reason.

`ReplayTests.A_replay_remembers_the_point_the_stroke_was_for` guards it.

### Honest positions, legible sizes

A ball is 92 mm on a court 30 m wide and a hoop wire is 6 mm. Drawn to scale
with the whole court in view that is a third of a pixel and a fifth of a pixel
— an empty green field with the game invisible on it. So balls have a floor in
screen pixels (`minBallPixels`), and hoops and pegs are drawn several times
life size with a pixel floor of their own (`minFurniturePixels`), all
recomputed as the camera zooms.

Hoops and pegs get **separate** exaggerations (`hoopScale`, `pegScale`). A hoop
wire is 6 mm against a 38 mm peg, so one shared multiplier leaves the hoops as
specks whenever the pegs look right — and croquet is a game about hoops.

Only the drawing is exaggerated, only ever upward, and only the size — never
the position, and never anything handed back to the simulation.

Anything sized as a share of the view uses `CourtCamera.ViewSpan`, the metres
across the **shorter** side of the screen, not the height. On a portrait view a
2:1 court is fitted by its width, which makes the view very tall — and a mallet
head that was a fixed share of that came out four times the size of the ball it
was addressing.

### Two traps, both already sprung

**Statics do not reset between play sessions.** The project runs with
`DisableDomainReload`, so a static cache survives entering and leaving play
mode. `Shapes` builds its textures with `HideFlags.DontSave`, and that flag
makes Unity destroy the *texture* on a play-mode transition while the `Sprite`
holding it survives — so a plain `sprite == null` check passes and hands back a
sprite with nothing in it. Every piece drawn from it renders as nothing, with no
error to say why. `Shapes.Spent` checks the texture, not the sprite.

**Three components draw onto one object.** Each keeps its pieces under a named
holder (`Court`, `Balls`, `Aim`) via `Shapes.Holder`, because each clears and
rebuilds its own and the first to do so would otherwise destroy the other two.

**Claude can drive the editor directly** through a Unity MCP connection
(`com.unity.ai.assistant`), when the editor is open. `Unity_RunCommand`
compiles and runs a C# script inside it — scene composition, components,
serialized fields, prefabs, project settings, play mode — with changes
registered for undo. There is also console access and a 2D scene capture, which
is how the court gets *looked* at rather than reasoned about.

Use it for what only exists as editor state. Scripts under `unity/Assets/` are
version-controlled source and get written to disk as normal; `RunCommand` is
for the scene and prefab wiring that would otherwise mean hand-editing YAML.
Note that each command is one-shot — nothing carries between calls — and any
recompile triggers a domain reload that interrupts whatever is in flight.

## Current state

Done and tested: rolling, contact, hoops and pegs as obstacles, the nine-wicket
layout, running hoops in the right direction, the turn and bonus-shot machinery
above, all four bonus ways, deadness, out-of-bounds replacement, points scored
for balls driven by others, staking out, sides and winning.

Also done: both rulebooks, the four difficulty levels above, and the lab front
end — aiming by pulling a mallet back, the four ways to take croquet with a
placement step, pause between strokes, and seats that decide which balls the
machine plays.

Also done: the groundwork for online play — `Stroke`, `Match`, `Checksum` and
`Reference`, with CoreCLR and Unity's Mono measured as landing on the same
number. Not built: the transport, the lobby, remote seats, or a server.

And the Unity game: the editor consuming `core/` as a local package, the court
drawn from the real `Field`, and a playable game on it — aiming by pulling a
mallet back, the machine taking its turns with the same furniture, shots
animated from `Replay`, a camera that follows the ball, and a HUD carrying the
turn, the stroke report and the feel dials.

The machine takes its aim with **the same furniture a person uses** — the same
ring, line and swing, drawn back the same distance. It sets the angle once and
does not move it again: a waggle over the ball reads as deliberation on a real
lawn, but from directly overhead, with the guide line and the ghost ring
swinging with it, it read as the machine changing its mind in the last second
before every stroke. It had already decided. An earlier version
showed it sighting several candidate lines instead; it was dropped because a
second visual language for the identical act made the opponent read as a
different kind of thing from the player.

### The swing, and where the stroke goes

There is no mallet drawn. Seen from directly above a mallet is a bar lying
across the line of the shot, and it read as a piece of furniture parked behind
the ball rather than as anything about to move. What a swing looks like from up
here is the head coming down the line — so it is a **chevron** drawn back along
the aim with a streak trailing behind it, and on release it travels into the
ball before the shot is played. Not a disc: anything ball-shaped back there
reads as a seventh ball. `Shapes.Chevron` is one tapered sprite rather than two
rotated bars, which meet at a hard corner and hold their thickness the whole way
out — the difference between something moving and a piece of clip art.

`AimGuide` is the pool-style trajectory aid. Two things about it.

**Its length is constant** — `AimControl.guideReach`, two and a half metres —
with a ghost ring at the end, stopping early only for something actually in the
way. It is a statement about the aim, not about the stroke.

That one number is also the radius of the patch the aim is sighted from, so the
line reaches exactly to its rim. They were two numbers once and drifted apart,
which left a line poking out of a circle that was supposed to bound it. The
patch is a faint even shading of the turf rather than a drawn ring — the ring
was the loudest thing on the court and is the least important — but it keeps a
clean edge, because showing where the aim stops is its whole job and a gradient
has no "where". It sits on `Layer.AimPatch`, under everything standing on the
grass; drawn over the top it dulled every ball it covered.

Two ways of getting that wrong, both made once. Keying it to the roll distance
made it shorten and grow while the pull was being set, which is exactly the
moment it needs to hold still: where a stroke is *aimed* and how far it would
*travel* are different questions, and the meter answers the second. Running it
to whatever is in the way however far off claims a precision the stroke has not
got — the angle is only as fine as the sighting that set it, a metre and a half
of lawn. Anything past a tap or a short roquet goes back to being judged, which
is the game.

**It is exact rather than estimated.** Friction here slows a ball without
bending it, so the path to the first contact is a ray test; and
`Sim.ResolvePair` gives equal masses an impulse along the line of centres, so
the struck ball leaves along that line and the striker keeps everything at right
angles to it plus what the restitution left. Both come out of the same numbers
the simulation uses. If the guide ever disagrees with what happens, one of the
two is wrong and it is worth finding out which — which is the point of not
approximating it.

**The two lines out of a contact are lengthened by the split of the blow.**
`AimGuide.OnwardRoll` and `CarriesRoll` are how far each ball rolls afterwards
as a share of what the striker would have rolled alone — *distances*, not
speeds, since distance goes as the square (`v² = 2ad`), and that square is what
makes a thin cut so lopsided: a striker glancing off at four fifths of its
speed keeps nearly two thirds of its roll while the ball it brushed gets a
hundredth. A full hit puts nearly everything into the ball in front and leaves
the striker a stub. That is the one thing worth knowing before playing a
contact — whether you are sending that ball somewhere or following it there.

They are **shares of a fixed budget**, never absolute distances, with a floor
of a tenth each so the short one still says which way it went. Absolute lengths
would tie the guide to the pull, and the whole reason this thing holds still is
that it stopped trying to answer "how hard" as well as "where".

### Setting the striker down is choosing an angle

Placing the ball for a bonus stroke moves it by **at most one ball width** —
two balls in contact is what a placement is — so at any sane zoom the drag
shifts the disc by a handful of pixels however far round it is swung. With
nothing but the two balls to look at, a placement that is working perfectly
reads as one that is ignoring you entirely. That is not a bug and there is
nothing to fix in the geometry: the ball is not the thing that needs to be
visible, the **line** is.

So while the striker is being set down, `AimControl.DrawAlignment` draws the
line of centres out to the boundary — where the other ball goes if it is then
struck straight. Unlike the aim line this one runs the whole way, because it is
not making the aim line's promise: where two balls are lined up is geometry and
exact at any distance, and it is the striking straight along it that is still
left to get right.

Not built yet: audio, touch gestures beyond what a single pointer gives, shot
preview, rovers and poison, "wicketed" balls, and the rule that a ball resting
within a mallet length of the boundary is brought in.

The scorekeeper app at `../Croquet Score App/index.html` was the original spec
for the course; `Course.Labels` here mirrors its `COURSE`.
