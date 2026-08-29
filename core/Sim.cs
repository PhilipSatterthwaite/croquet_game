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
        /// <summary>
        /// Advances the whole world: balls, hoops, pegs, and the crossing tally
        /// that decides whether a hoop was run. This is the one the game uses.
        /// </summary>
        public static bool Step(World w, double dt) => Step(w.Balls, w.Spec, dt, w);

        /// <summary>
        /// Runs the shot to completion on a full world. Returns the number of
        /// frames it took.
        /// </summary>
        public static int Settle(World w, double dt = 1.0 / 120.0, int maxSteps = 100000)
        {
            int n = 0;
            while (n < maxSteps && Step(w, dt)) n++;
            return n;
        }

        /// <summary>
        /// Bare-court overload: no hoops, no pegs, nothing to count. Useful for
        /// isolating rolling and contact behaviour in tests.
        /// </summary>
        public static bool Step(Ball[] balls, CourtSpec c, double dt) => Step(balls, c, dt, null);

        static bool Step(Ball[] balls, CourtSpec c, double dt, World w)
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
            for (int s = 0; s < sub; s++) Substep(balls, c, h, w);

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

        static void Substep(Ball[] balls, CourtSpec c, double h, World w)
        {
            // Where everything was before it moved. Crossing a hoop is an event
            // between two positions, not a property of either one, so the
            // previous position has to survive the move to be compared against.
            Vec2[] prev = null;
            if (w != null)
            {
                prev = new Vec2[balls.Length];
                for (int i = 0; i < balls.Length; i++) prev[i] = balls[i].Pos;
            }

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

            if (w != null) w.Step++;

            // Six balls at most, so every pair is cheaper than any structure
            // that would avoid testing them.
            for (int i = 0; i < balls.Length; i++)
            {
                if (!balls[i].InPlay) continue;
                for (int j = i + 1; j < balls.Length; j++)
                {
                    if (!balls[j].InPlay) continue;
                    if (ResolvePair(ref balls[i], ref balls[j], c)) w?.NoteContact(i, j);
                }
            }

            if (w != null)
            {
                for (int i = 0; i < balls.Length; i++)
                {
                    if (!balls[i].InPlay) continue;
                    foreach (var hoop in w.Field.Hoops)
                    {
                        Deflect(ref balls[i], hoop.LeftPost, hoop.WireRadius, c);
                        Deflect(ref balls[i], hoop.RightPost, hoop.WireRadius, c);
                    }
                    if (Deflect(ref balls[i], w.Field.HomePeg, w.Field.PegRadius, c))
                        w.NotePeg(i, Field.HomePegPoint);
                    if (Deflect(ref balls[i], w.Field.TurningPeg, w.Field.PegRadius, c))
                        w.NotePeg(i, Field.TurningPegPoint);
                }
            }

            for (int i = 0; i < balls.Length; i++)
            {
                if (!balls[i].InPlay) continue;
                bool wasOut = balls[i].WentOut;
                ClampToLawn(ref balls[i], c);
                if (w != null && balls[i].WentOut && !wasOut) w.NoteOut(i);
            }

            if (w != null)
                for (int i = 0; i < balls.Length; i++)
                    if (balls[i].InPlay) CountCrossings(w, i, prev[i], balls[i].Pos);
        }

        /// <summary>
        /// Bounces a ball off a fixed circle -- a hoop upright or a peg. The
        /// obstacle does not move, so all the energy that is not returned is
        /// simply lost, which is why wire and peg get their own restitution:
        /// a wicket absorbs far more than another ball does.
        /// </summary>
        static bool Deflect(ref Ball ball, Vec2 at, double radius, CourtSpec c)
        {
            Vec2 delta = ball.Pos - at;
            double min = c.BallRadius + radius;
            double distSq = delta.LengthSq;
            if (distSq >= min * min) return false;

            // Dead centre on the obstacle: no normal to work with. Nudge it up
            // the court rather than dividing by zero.
            Vec2 n = distSq > 0 ? delta / Math.Sqrt(distSq) : new Vec2(1, 0);

            ball.Pos = at + n * min;

            double vn = ball.Vel.Dot(n);
            if (vn < 0) ball.Vel -= n * ((1 + c.ObstacleRestitution) * vn);
            return true;
        }

        /// <summary>
        /// Tallies signed crossings of each hoop's plane. Only a crossing that
        /// happens BETWEEN the uprights counts, which is what separates running
        /// a hoop from rolling past the outside of it; and the sign is what
        /// makes a ball that goes through and comes straight back out score
        /// nothing, because the two cancel.
        /// </summary>
        static void CountCrossings(World w, int ball, Vec2 from, Vec2 to)
        {
            var hoops = w.Field.Hoops;
            for (int h = 0; h < hoops.Length; h++)
            {
                double before = from.X - hoops[h].Center.X;
                double after = to.X - hoops[h].Center.X;

                int dir;
                if (before <= 0 && after > 0) dir = 1;
                else if (before >= 0 && after < 0) dir = -1;
                else continue;

                // Where along y it crossed, so a ball passing wide of the hoop
                // on the same plane is not credited with running it.
                double t = (before - after) == 0 ? 0 : before / (before - after);
                double yAt = from.Y + (to.Y - from.Y) * t;

                if (Math.Abs(yAt - hoops[h].Center.Y) < hoops[h].HalfGap)
                    w.NoteCross(ball, h, dir);
            }
        }

        /// <summary>Returns true if the two were in contact this substep.</summary>
        static bool ResolvePair(ref Ball a, ref Ball b, CourtSpec c)
        {
            Vec2 delta = b.Pos - a.Pos;
            double distSq = delta.LengthSq;
            double min = c.BallRadius * 2;
            if (distSq >= min * min || distSq <= 0) return false;

            double dist = Math.Sqrt(distSq);
            Vec2 n = delta / dist;

            // Push them apart before the impulse, or the next substep starts
            // with them still interpenetrating and they stick together.
            double overlap = (min - dist) * 0.5;
            a.Pos -= n * overlap;
            b.Pos += n * overlap;

            double closing = (b.Vel - a.Vel).Dot(n);
            if (closing > 0) return true;   // already separating; the overlap was enough

            // Equal masses, so the impulse splits evenly.
            double jImpulse = -(1 + c.Restitution) * closing * 0.5;
            a.Vel -= n * jImpulse;
            b.Vel += n * jImpulse;
            return true;
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
