using System;
using System.Collections.Generic;

namespace Croquet.Core
{
    /// <summary>
    /// A stroke, played for real, together with the frames that show it happening.
    ///
    /// A deterministic simulation knows the outcome the moment the ball is
    /// struck, so the game does not animate its way towards an answer: it
    /// settles the shot, applies the rules, and then rebuilds the same shot
    /// frame by frame purely for the eye. Every front end works this way -- the
    /// lab, Unity, and eventually online play, which sends the stroke and lets
    /// both ends arrive at the same place on their own.
    ///
    /// This lives in core rather than in a front end because there is exactly
    /// one correct way to do it and two copies would drift. The failure that
    /// matters is not a wrong picture: it is an animation that shows something
    /// the rules did not do.
    ///
    /// The frames are stepped at <see cref="StepDt"/>, which is the step
    /// <see cref="Sim.Settle(World, double, int)"/> uses, and sampled every
    /// <see cref="SubstepsPerFrame"/> of them. That is not a detail. Sim
    /// subdivides each step according to the fastest ball, so a coarser step
    /// integrates a different sequence and lands somewhere else -- the ball
    /// would finish the animation a few centimetres from where the rules had
    /// already put it, and jump on the last frame.
    /// <c>ReplayTests.The_last_frame_is_where_the_ball_actually_finished</c>
    /// holds this to exact equality.
    /// </summary>
    public sealed class Replay
    {
        /// <summary>The step the frames are built with. Matches Sim.Settle's default.</summary>
        public const double StepDt = 1.0 / 120.0;

        /// <summary>Two substeps to a frame, giving 60 frames a second of playback.</summary>
        public const int SubstepsPerFrame = 2;

        public const int FramesPerSecond = 60;

        /// <summary>A minute of animation. A shot this long has something wrong with it.</summary>
        public const int MaxFrames = 3600;

        /// <summary>What the stroke did. The game is already updated when this is handed back.</summary>
        public readonly StrokeResult Result;

        /// <summary>Ball positions, one entry per ball, frame by frame. The first is the start.</summary>
        public readonly IReadOnlyList<Vec2[]> Frames;

        /// <summary>Whose stroke it was.</summary>
        public readonly int Striker;

        /// <summary>
        /// The point the striker was playing for when the ball was struck.
        ///
        /// The rules are applied the instant a stroke is played, so by the time
        /// there is anything to animate the Game has already moved on -- and an
        /// interface reading it live announces the next hoop while the ball is
        /// still rolling towards this one. This is what the shot was ABOUT, and
        /// it stays true for as long as the shot is on screen.
        /// </summary>
        public readonly int PointBefore;

        /// <summary>
        /// The ball actually set moving, which is the striker except on a foot
        /// shot: there the striker is held and the roqueted ball is driven.
        /// This is the one a camera should follow.
        /// </summary>
        public readonly int Struck;

        /// <summary>Where the struck ball started, after any bonus placement.</summary>
        public readonly Vec2 From;

        /// <summary>Whether it was played as an ordinary stroke or the bonus after a roquet.</summary>
        public readonly StrokeKind Kind;

        public readonly Vec2 Aim;
        public readonly double Power;

        /// <summary>
        /// Every ball's rules-side state from before the stroke: deadness, and
        /// how far round the course. For the same reason as
        /// <see cref="PointBefore"/> -- the Game has already moved on, and an
        /// interface reading it live shows a roquet's deadness, or a wicket's
        /// revival, as the mallet meets the ball.
        /// </summary>
        public readonly BallState[] Before;

        /// <summary>
        /// The frame on which the striker's deadness changes: the contact that
        /// roquets a ball, or the crossing that clears its wicket and revives
        /// it. The last frame when the stroke changed neither.
        ///
        /// Read off the real stroke's events, whose substep stamps map onto
        /// these frames exactly, because the frames are stepped the same way.
        /// </summary>
        public readonly int DeadnessFrame;

        /// <summary>
        /// The ball Option 11 was protecting when the stroke was struck, or -1.
        /// Ending a turn works it out afresh, so by the time the next player's
        /// ball is rolling the Game has already forgotten it.
        /// </summary>
        public readonly int WicketedBefore;

        public int FrameCount => Frames.Count;
        public double Seconds => (Frames.Count - 1) / (double)FramesPerSecond;

        Replay(StrokeResult result, IReadOnlyList<Vec2[]> frames, int striker, int pointBefore,
               int struck, Vec2 from, StrokeKind kind, Vec2 aim, double power,
               BallState[] before, int deadnessFrame, int wicketedBefore)
        {
            Result = result;
            Frames = frames;
            Striker = striker;
            PointBefore = pointBefore;
            Struck = struck;
            From = from;
            Kind = kind;
            Aim = aim;
            Power = power;
            Before = before;
            DeadnessFrame = deadnessFrame;
            WicketedBefore = wicketedBefore;
        }

        /// <summary>Where every ball comes to rest in the animation.</summary>
        public Vec2[] LastFrame => Frames[Frames.Count - 1];

        /// <summary>An ordinary stroke, struck from where the ball lies.</summary>
        public static Replay Play(Game game, Vec2 aim, double power) =>
            Run(game, false, default, Vec2.Zero, aim, power);

        /// <summary>The first bonus stroke after a roquet, taken one of the four ways.</summary>
        public static Replay PlayBonus(Game game, BonusWay way, Vec2 placement,
                                       Vec2 aim, double power) =>
            Run(game, true, way, placement, aim, power);

        /// <summary>A stroke the bot chose, played through the same path a person's is.</summary>
        public static Replay Play(Game game, BotMove move) =>
            Play(game, Stroke.Of(move));

        /// <summary>
        /// A stroke as a value -- what a saved match, a replay and the other end
        /// of a network connection all hand over.
        /// </summary>
        public static Replay Play(Game game, Stroke s) =>
            s.IsBonus
                ? PlayBonus(game, s.Way, s.Placement, s.Aim, s.Power)
                : Play(game, s.Aim, s.Power);

        /// <summary>
        /// Whichever way the stroke is taken, the shape is the same: note where
        /// everything stands, let the Game apply the rules, then rebuild the
        /// identical shot on a scratch world for the animation.
        /// </summary>
        static Replay Run(Game game, bool bonus, BonusWay way, Vec2 placement,
                          Vec2 aim, double power)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));

            var world = game.World;
            int striker = game.Striker;
            int pointBefore = game.States[striker].Point;
            StrokeKind kind = game.Stroke;

            // Read before the stroke: resolving it clears the roqueted ball and
            // moves the turn on, and the frames are about the world as it was.
            var before = new Vec2[world.Balls.Length];
            var wasInPlay = new bool[world.Balls.Length];
            for (int i = 0; i < before.Length; i++)
            {
                before[i] = world.Balls[i].Pos;
                wasInPlay[i] = world.Balls[i].InPlay;
            }

            int wicketed = game.Wicketed;
            var states = new BallState[game.States.Length];
            for (int i = 0; i < states.Length; i++) states[i] = game.States[i].Clone();

            int struck = striker;
            StrokeResult result;

            // Read now: resolving the stroke forgets which ball was roqueted.
            int croquetWith = bonus && way == BonusWay.CroquetShot ? game.RoquetedBall : -1;

            if (bonus)
            {
                if (kind != StrokeKind.Bonus)
                    throw new InvalidOperationException("no bonus stroke is owed");

                // The striker is set down against the roqueted ball before it is
                // played, so that -- not where it came to rest -- is where the
                // animation starts it from.
                before[striker] = game.BonusPlacement(way, placement);
                if (way == BonusWay.FootShot) struck = game.RoquetedBall;

                result = game.PlayBonus(way, placement, aim, power);
            }
            else
            {
                if (kind == StrokeKind.Bonus)
                    throw new InvalidOperationException("a bonus stroke is owed; use PlayBonus");

                result = game.Play(aim, power);
            }

            var frames = Frame(before, wasInPlay, struck, aim, power, world, croquetWith,
                               out var steps);

            // The world still holds the events of the stroke just resolved.
            int deadness = FrameOf(steps, DeadnessStep(world, striker, result));

            return new Replay(result, frames, striker, pointBefore, struck, before[struck],
                              kind, aim, power, states, deadness, wicketed);
        }

        /// <summary>
        /// The same start, the same strike, stepped the same way -- so it lands
        /// exactly where the rules have already put it. Nothing here touches the
        /// real world; the balls are copies and the scratch world exists only to
        /// carry the tallies Sim wants to write.
        /// </summary>
        static List<Vec2[]> Frame(Vec2[] before, bool[] wasInPlay, int struck,
                                  Vec2 aim, double power, World real, int croquetWith,
                                  out List<int> steps)
        {
            var balls = new Ball[before.Length];
            for (int i = 0; i < balls.Length; i++)
            {
                balls[i] = new Ball(before[i]);
                balls[i].InPlay = wasInPlay[i];
            }

            var scratch = new World(balls, real.Field, real.Spec);
            scratch.ClearShot();

            // The same follow-through the rules played the stroke with, or the
            // film would leave the striker short of where it actually stopped
            // and it would jump on the last frame.
            if (croquetWith >= 0) scratch.TakeCroquet(struck, croquetWith);

            balls[struck].Vel = aim.Normalized * power;

            var frames = new List<Vec2[]>(256) { Snap(balls) };

            // The substep count reached by each frame, so an event's stamp can
            // be turned into the frame it happens on.
            steps = new List<int>(256) { scratch.Step };

            bool moving = true;
            while (moving && frames.Count < MaxFrames)
            {
                for (int s = 0; s < SubstepsPerFrame && moving; s++)
                    moving = Sim.Step(scratch, StepDt);

                frames.Add(Snap(balls));
                steps.Add(scratch.Step);
            }

            return frames;
        }

        /// <summary>
        /// The substep the striker's deadness changed on, or int.MaxValue if
        /// the stroke did not change it.
        /// </summary>
        static int DeadnessStep(World world, int striker, StrokeResult r)
        {
            int at = int.MaxValue;

            // A foul put everything back; nothing about deadness happened.
            if (r.WicketedFoul >= 0 || r.DeadFoul >= 0) return at;

            if (r.Roqueted >= 0)
                foreach (var e in world.Events)
                    if (e.Kind == EventKind.BallContact && (e.Ball == striker || e.Other == striker))
                    {
                        at = e.Step;
                        break;
                    }

            // The first WICKET scored revives; the turning stake revives nobody.
            foreach (int point in r.PointsScored)
            {
                if (world.Field.IsPeg(point)) continue;
                int s = world.StepRanPoint(striker, point);
                if (s >= 0 && s < at) at = s;
                break;
            }

            return at;
        }

        /// <summary>The first frame that has reached this substep; the last if none has.</summary>
        static int FrameOf(List<int> steps, int step)
        {
            for (int k = 0; k < steps.Count; k++)
                if (steps[k] >= step) return k;
            return steps.Count - 1;
        }

        static Vec2[] Snap(Ball[] balls)
        {
            var f = new Vec2[balls.Length];
            for (int i = 0; i < balls.Length; i++) f[i] = balls[i].Pos;
            return f;
        }
    }
}
