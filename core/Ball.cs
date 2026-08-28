namespace Croquet.Core
{
    /// <summary>
    /// A ball as the physics cares about it: where it is and how fast it is
    /// going. Everything else a ball has -- which hoop it is for, what it is
    /// dead on, whose side it is -- belongs to the rules layer, which is a
    /// separate thing that never runs inside the physics loop.
    /// </summary>
    public struct Ball
    {
        public Vec2 Pos;
        public Vec2 Vel;

        /// <summary>False once a ball has pegged out and left the lawn.</summary>
        public bool InPlay;

        /// <summary>
        /// Set when the centre crossed the boundary during the current shot.
        /// The simulation stops the ball on the line and records this; where it
        /// comes back on is a rule, not physics, so the rules layer decides.
        /// </summary>
        public bool WentOut;

        public Ball(Vec2 pos)
        {
            Pos = pos;
            Vel = Vec2.Zero;
            InPlay = true;
            WentOut = false;
        }

        public bool Moving => Vel.LengthSq > 0;
    }
}
