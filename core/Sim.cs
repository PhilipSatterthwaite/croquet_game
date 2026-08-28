using System;

namespace Croquet.Core
{
    /// <summary>
    /// The physics. Balls are equal-mass circles rolling on a plane, which is
    /// what croquet actually is once you accept that the lawn is flat.
    ///
    /// Two properties are worth protecting as this grows:
    ///
    /// 1. Determinism. Same input, same output, on every device -- see Vec2 for
    ///    why that holds. It is what lets a shot be sent over the network as
    ///    three numbers instead of a stream of positions.
    /// 2. No engine types. Nothing here knows what Unity is, so all of it runs
    ///    under `dotnet test` in milliseconds, and an AI can play out thousands
    ///    of candidate shots without rendering a frame.
    /// </summary>
    public static class Sim
    {
        /// <summary>
        /// A substep may not move a ball more than this fraction of its radius.
        /// Collisions are resolved by overlap, so a ball that jumps further than
        /// its own size in one step can pass clean through another and miss the
        /// contact entirely. At a hard 12 m/s strike and a 60 Hz frame that is
        /// 20 cm of travel against a 4.6 cm ball, so this matters immediately.
        /// </summary>
        const double MaxTravelPerRadius = 0.5;

        /// <summary>
        /// Advances every ball by dt, subdividing internally as much as the
        /// fastest ball requires. Returns true while anything is still moving,
        /// so a caller can drive it from a frame loop and know when the shot is
        /// over.
        /// </summary>
        public static bool Step(Ball[] balls, CourtSpec c, double dt)
        {
            if (balls == null) throw new ArgumentNullException(nameof(balls));
            if (c == null) throw new ArgumentNullException(nameof(c));
            if (dt <= 0) return AnyMoving(balls);

            double fastest = 0;
            for (int i = 0; i < balls.Length; i++)
            {
                if (!balls[i].InPlay) continue;
                double sq = balls[i].Vel.LengthSq;
                if (sq > fastest) fastest = sq;
            }
            if (fastest <= 0) return false;
            fastest = Math.Sqrt(fastest);

            double maxTravel = c.BallRadius * MaxTravelPerRadius;
            int sub = (int)Math.Ceiling(fastest * dt / maxTravel);
            if (sub < 1) sub = 1;

            double h = dt / sub;
            for (int s = 0; s < sub; s++) Substep(balls, c, h);

            return AnyMoving(balls);
        }

        /// <summary>
        /// Runs the shot to completion and returns the number of steps it took.
        /// This is the entry point for the AI and for tests -- neither wants to
        /// wait on a frame loop. maxSteps stops a runaway from hanging: if it
        /// trips, something is wrong with the friction model rather than with
        /// the shot.
        /// </summary>
        public static int Settle(Ball[] balls, CourtSpec c, double dt = 1.0 / 120.0,
                                 int maxSteps = 100000)
        {
            int n = 0;
            while (n < maxSteps && Step(balls, c, dt)) n++;
            return n;
        }

        static bool AnyMoving(Ball[] balls)
        {
            for (int i = 0; i < balls.Length; i++)
                if (balls[i].InPlay && balls[i].Moving) return true;
            return false;
        }

        static void Substep(Ball[] balls, CourtSpec c, double h)
        {
            // Friction first, then move: taking the drop off the velocity before
            // it is integrated keeps a ball from overshooting on its last step.
            for (int i = 0; i < balls.Length; i++)
            {
                if (!balls[i].InPlay || !balls[i].Moving) continue;

                double speed = balls[i].Vel.Length;
                double drop = c.Friction * h;
                if (speed - drop < c.SleepSpeed) balls[i].Vel = Vec2.Zero;
                else balls[i].Vel = balls[i].Vel * ((speed - drop) / speed);

                balls[i].Pos += balls[i].Vel * h;
            }

            // Six balls at most, so every pair is cheaper than any structure
            // that would avoid testing them.
            for (int i = 0; i < balls.Length; i++)
            {
                if (!balls[i].InPlay) continue;
                for (int j = i + 1; j < balls.Length; j++)
                {
                    if (!balls[j].InPlay) continue;
                    ResolvePair(ref balls[i], ref balls[j], c);
                }
            }

            for (int i = 0; i < balls.Length; i++)
            {
                if (!balls[i].InPlay) continue;
                ClampToLawn(ref balls[i], c);
            }
        }

        static void ResolvePair(ref Ball a, ref Ball b, CourtSpec c)
        {
            Vec2 delta = b.Pos - a.Pos;
            double distSq = delta.LengthSq;
            double min = c.BallRadius * 2;
            if (distSq >= min * min || distSq <= 0) return;

            double dist = Math.Sqrt(distSq);
            Vec2 n = delta / dist;

            // Push them apart before the impulse, or the next substep starts
            // with them still interpenetrating and they stick together.
            double overlap = (min - dist) * 0.5;
            a.Pos -= n * overlap;
            b.Pos += n * overlap;

            double closing = (b.Vel - a.Vel).Dot(n);
            if (closing > 0) return;    // already separating; the overlap was enough

            // Equal masses, so the impulse splits evenly.
            double jImpulse = -(1 + c.Restitution) * closing * 0.5;
            a.Vel -= n * jImpulse;
            b.Vel += n * jImpulse;
        }

        /// <summary>
        /// A ball is out when its centre crosses the line -- that is the actual
        /// croquet rule, not an approximation, which is why the ball is stopped
        /// on the line rather than bounced off it. Bringing it back in is the
        /// rules layer's job.
        /// </summary>
        static void ClampToLawn(ref Ball ball, CourtSpec c)
        {
            double x = ball.Pos.X, y = ball.Pos.Y;
            bool out_ = false;

            if (x < 0) { x = 0; out_ = true; }
            else if (x > c.Width) { x = c.Width; out_ = true; }

            if (y < 0) { y = 0; out_ = true; }
            else if (y > c.Height) { y = c.Height; out_ = true; }

            if (!out_) return;

            ball.Pos = new Vec2(x, y);
            ball.Vel = Vec2.Zero;
            ball.WentOut = true;
        }
    }
}
