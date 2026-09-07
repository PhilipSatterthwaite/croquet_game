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
    /// What it does NOT include is anything hand-computed about value: no
    /// "distance to my hoop", no "am I in front of it". Those are exactly what
    /// the linear evaluator had to be told and what a network is for working
    /// out. The bare geometry goes in; the judgement comes out.
    /// </summary>
    public static class Sight
    {
        /// <summary>Balls the encoding has room for. Six is the whole game.</summary>
        public const int MaxBalls = 6;

        /// <summary>Numbers describing each ball.</summary>
        public const int PerBall = 9;

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
                    into[at + 5] = (target.X - pos.X) / across;
                    into[at + 6] = (target.Y - pos.Y) / across;
                }

                // Deadness, both ways round, which is the whole of what a
                // position owes to the strokes played before it.
                into[at + 7] = game.States[me].Dead.Contains(ball) ? 1 : 0;
                into[at + 8] = state.Dead.Contains(me) ? 1 : 0;
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

        /// <summary>A fresh buffer of the right size, for callers that want one.</summary>
        public static double[] Buffer() => new double[Size];
    }
}
