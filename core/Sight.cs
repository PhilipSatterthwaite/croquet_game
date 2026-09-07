using System;

namespace Croquet.Core
{
    /// <summary>
    /// A position as numbers a network can read.
    ///
    /// This is the single most important decision in the whole neural approach
    /// and the one least often thought about. A network cannot learn what it
    /// cannot see, and it cannot generalise across things the encoding does not
    /// make look alike. Three rules follow, and all three are load-bearing:
    ///
    /// **Everything is from the striker's point of view.** Not "ball 0's
    /// position and ball 1's position" but "my position, my partner's, my
    /// opponents'". Encoded absolutely, the network would have to learn the
    /// game once for each ball and could not carry a single thing it learned as
    /// blue over to playing as red. Encoded relatively, every position is one
    /// example of one game.
    ///
    /// **Everything is scaled to about minus one to one.** Positions divided by
    /// the court, course points by their total, distances by the court's
    /// diagonal. A network fed metres alongside a flag that is 0 or 1 spends
    /// its first thousand steps discovering that they are not the same kind of
    /// number, and sometimes never does.
    ///
    /// **Absence is explicit.** A ball not in play gets a flag saying so, not a
    /// position of zero -- which is a real corner of the lawn and would teach
    /// the network that finished balls congregate there.
    ///
    /// ## Why there are croquet features in here now
    ///
    /// This used to hold bare geometry on purpose, and said so: no "distance to
    /// my hoop", no "am I in front of it", because those are what the linear
    /// evaluator had to be TOLD and what a network is for working out. The
    /// bare geometry goes in; the judgement comes out.
    ///
    /// That is the right principle at AlphaZero's data budget and the wrong one
    /// at ours. Learning "in front of the hoop" from two raw coordinates and a
    /// target vector means discovering a rotation, a sign convention and a
    /// two-sided threshold, from scratch, from a few thousand games -- and a
    /// plain MLP over coordinates has no structure that makes any of that
    /// cheap, the way a convolution over a board does. Every one of those the
    /// encoding hands over is capacity spent on strategy instead.
    ///
    /// So the additions are all things a player would SAY, and they are all
    /// still facts rather than opinions. Where the hoop's direction puts a ball
    /// (along and across, in the hoop's own frame) is geometry. Whether a ball
    /// is a rover is the rules. Whether a line between two balls is clear is a
    /// ray test. What any of it is WORTH is still entirely the network's to
    /// decide -- which is the line that matters, and none of these cross it.
    ///
    /// The rule that stays: nothing in here may be identical for every
    /// candidate stroke in a turn. That is what made the first net useless --
    /// it was handed the scoreboard, fitted it, predicted winners well and
    /// scored every candidate alike. A feature that cannot change when a
    /// stroke is played teaches the network about the game and nothing about
    /// the choice.
    /// </summary>
    public static class Sight
    {
        /// <summary>Balls the encoding has room for. Six is the whole game.</summary>
        public const int MaxBalls = 6;

        /// <summary>Numbers describing each ball.</summary>
        public const int PerBall = 16;

        /// <summary>...and the few that describe the turn rather than a ball.</summary>
        public const int Shared = 3;

        public const int Size = MaxBalls * PerBall + Shared;

        /// <summary>
        /// Writes the position into <paramref name="into"/>, as seen by
        /// <paramref name="me"/>.
        ///
        /// Balls come in a fixed order -- me first, then my partners, then my
        /// opponents, each group in playing order -- so "the first block is the
        /// ball I am playing" is true in every example the network ever sees.
        /// </summary>
        public static void Read(Game game, int me, double[] into)
        {
            if (into == null || into.Length < Size)
                throw new ArgumentException($"needs {Size} numbers", nameof(into));

            Array.Clear(into, 0, Size);

            var world = game.World;
            var field = world.Field;
            var court = world.Spec;

            double width = Math.Max(1e-6, court.Width);
            double height = Math.Max(1e-6, court.Height);
            double across = Math.Sqrt(width * width + height * height);

            var mine = world.Balls[me].Pos;
            bool meOn = world.Balls[me].InPlay && !game.States[me].Finished;

            int slot = 0;

            // Me, then partners, then opponents. Two passes over the balls
            // rather than a sort, so the order inside each group is playing
            // order and stays the same from one position to the next.
            Write(me);
            for (int i = 0; i < world.Balls.Length && slot < MaxBalls; i++)
                if (i != me && Bot.SameSide(game, me, i)) Write(i);
            for (int i = 0; i < world.Balls.Length && slot < MaxBalls; i++)
                if (i != me && !Bot.SameSide(game, me, i)) Write(i);

            void Write(int ball)
            {
                int at = slot++ * PerBall;
                var state = game.States[ball];
                bool on = world.Balls[ball].InPlay && !state.Finished;

                into[at] = on ? 1 : 0;
                if (!on)
                {
                    // Finished is a different thing from not yet started, and
                    // the difference decides whether it is still a threat.
                    into[at + 1] = state.Finished ? 1 : -1;
                    return;
                }

                var pos = world.Balls[ball].Pos;
                into[at + 1] = 0;
                into[at + 2] = pos.X / width * 2 - 1;
                into[at + 3] = pos.Y / height * 2 - 1;

                // How far round the course, and how far it still has to go.
                int point = state.Point;
                into[at + 4] = point / (double)Math.Max(1, state.Total) * 2 - 1;

                if (!field.IsFinished(point))
                {
                    var target = field.TargetFor(point);
                    var away = target - pos;

                    into[at + 5] = away.X / across;
                    into[at + 6] = away.Y / across;

                    // The magnitude as well as the components. It is one square
                    // root the network would otherwise have to approximate with
                    // hidden units, in every position, before it could express
                    // anything at all about being near a hoop.
                    into[at + 9] = away.Length / across;

                    // Where the ball stands in the HOOP'S frame rather than the
                    // court's: how far it still has to come along the running
                    // direction, and how far off the line of it. This is what
                    // "in front of the hoop" means, and the pair of numbers is
                    // handed over raw rather than cooked into a single score,
                    // so how deep and how wide that zone is stays the
                    // network's to decide rather than mine.
                    //
                    // Negative "along" is the far side: past it, and needing to
                    // come back. A peg has no direction and leaves both at zero.
                    if (!field.IsPeg(point))
                    {
                        int dir = field.DirectionFor(point);
                        into[at + 10] = (target.X - pos.X) * dir / across;
                        into[at + 11] = Math.Abs(pos.Y - target.Y) / across;
                    }
                }

                // Deadness, both ways round, which is the whole of what a
                // position owes to the strokes played before it.
                into[at + 7] = game.States[me].Dead.Contains(ball) ? 1 : 0;
                into[at + 8] = state.Dead.Contains(me) ? 1 : 0;

                // A rover: every hoop run, only the finishing peg left. Worth
                // naming because the rules make it a different kind of ball --
                // it can no longer score off anything but the peg, and pegging
                // out takes it out of the game entirely, which is a decision
                // rather than an achievement when a partner is still going.
                into[at + 12] = point == state.Total - 1 ? 1 : 0;

                // How much of the field this ball has already used up. A ball
                // dead on everything has a turn worth very little, and that is
                // invisible in any amount of geometry.
                into[at + 13] = state.Dead.Count / (double)Math.Max(1, MaxBalls - 1);

                // And what it is to ME: how far away, and whether anything is
                // standing in between. These two are where roquets, splits and
                // wiring live -- the whole question of what is available this
                // turn and what has been taken away.
                if (meOn && ball != me)
                {
                    into[at + 14] = (pos - mine).Length / across;
                    into[at + 15] = Clear(world, mine, pos) ? 1 : 0;
                }
            }

            // The turn itself.
            int shared = MaxBalls * PerBall;
            into[shared] = game.Striker == me ? 1 : Bot.SameSide(game, me, game.Striker) ? 0.5 : -1;
            into[shared + 1] = Math.Min(3, game.ShotsLeft) / 3.0;
            into[shared + 2] = game.Stroke == StrokeKind.Bonus ? 1 : 0;

            // There WAS a summary of the race here -- my side's progress,
            // theirs, and the difference -- on the grounds that a game is won
            // or lost on exactly that. It is true, and it is the reason the
            // first net was useless.
            //
            // Those three numbers are IDENTICAL for every candidate stroke in a
            // turn: one stroke does not move the scoreboard. They are the
            // easiest possible signal for predicting a winner and carry no
            // information at all for choosing between strokes, so a network
            // given them fits them, predicts winners well, and scores every
            // candidate alike. Measured: 0.07 of spread across 144 wildly
            // different shots. Taking them out forces it to read the positions,
            // which is the only place the difference between two strokes lives.
        }

        /// <summary>
        /// Is the line from <paramref name="a"/> to <paramref name="b"/> free of
        /// anything a ball would hit on the way?
        ///
        /// A roquet that cannot be played is not an opportunity, and a ball that
        /// cannot be reached is safe from you -- which is the same fact read
        /// from either end. Croquet has a word for the deliberate version of it,
        /// wiring, and no amount of coordinates says whether a hoop leg is in
        /// the way.
        ///
        /// Approximate on purpose: it asks whether the CENTRE line is blocked,
        /// with a clearance, rather than whether some cut shot could still
        /// squeeze past. A ball that has to thread a hoop's legs to make contact
        /// is one the network is right to think twice about.
        /// </summary>
        static bool Clear(World w, Vec2 a, Vec2 b)
        {
            var away = b - a;
            double len = away.Length;
            if (len < 1e-9) return true;

            var unit = away / len;
            double r = w.Spec.BallRadius;
            var field = w.Field;

            for (int i = 0; i < w.Balls.Length; i++)
            {
                if (!w.Balls[i].InPlay) continue;

                // The two ends are the balls being asked about, not obstacles
                // between them. Compared by position rather than by index
                // because that is all this is handed.
                var p = w.Balls[i].Pos;
                if ((p - a).LengthSq < 1e-12 || (p - b).LengthSq < 1e-12) continue;

                if (Blocks(p, r + r)) return false;
            }

            for (int h = 0; h < field.Hoops.Length; h++)
            {
                var hoop = field.Hoops[h];
                double gap = r + hoop.WireRadius;
                if (Blocks(hoop.LeftPost, gap) || Blocks(hoop.RightPost, gap)) return false;
            }

            for (int p = 0; p < field.Pegs.Length; p++)
                if (Blocks(field.Pegs[p], r + field.PegRadius)) return false;

            return true;

            // How near the segment this thing comes. Only what lies BETWEEN the
            // two ends counts: something beside the line, or behind either ball,
            // is not in the way of anything.
            bool Blocks(Vec2 c, double clearance)
            {
                double along = (c - a).Dot(unit);
                if (along <= 0 || along >= len) return false;
                return (c - (a + unit * along)).LengthSq < clearance * clearance;
            }
        }

        /// <summary>A fresh buffer of the right size, for callers that want one.</summary>
        public static double[] Buffer() => new double[Size];
    }
}
