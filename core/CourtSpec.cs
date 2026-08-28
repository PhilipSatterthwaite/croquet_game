namespace Croquet.Core
{
    /// <summary>
    /// Every dimension and constant the simulation uses, in SI units. Nothing
    /// here is hard-coded into the sim, so a lawn can be re-sized or the grass
    /// made faster without touching physics code -- and a test can build a tiny
    /// court to exercise a case that would take a long roll on a real one.
    ///
    /// Defaults describe a backyard nine-wicket court: 100 by 50 feet, with
    /// tournament-size balls.
    /// </summary>
    public sealed class CourtSpec
    {
        /// <summary>Long axis, metres. 100 ft.</summary>
        public double Width = 30.48;

        /// <summary>Short axis, metres. 50 ft.</summary>
        public double Height = 15.24;

        /// <summary>Ball radius, metres. Tournament balls are 3-5/8 in across.</summary>
        public double BallRadius = 0.046;

        /// <summary>
        /// Rolling deceleration, metres per second squared. This is the single
        /// number that decides how the lawn "feels": lower is a fast, keen
        /// lawn, higher is heavy grass. Around 0.6 is a decent mown lawn.
        /// </summary>
        public double Friction = 0.6;

        /// <summary>
        /// Coefficient of restitution between two balls. Croquet balls are hard
        /// and lose little in a clean strike.
        /// </summary>
        public double Restitution = 0.8;

        /// <summary>
        /// Below this speed a ball is treated as stopped, in metres per second.
        /// Without it, friction leaves balls creeping forever at ever-smaller
        /// velocities and a turn never ends.
        /// </summary>
        public double SleepSpeed = 0.02;

        /// <summary>
        /// A ball whose centre leaves the lawn is out. In nine-wicket play it
        /// comes back a set distance in from where it crossed; the simulation
        /// only records the crossing, and the rules layer does the placing.
        /// </summary>
        public double BoundaryReturn = 0.3048;   // one foot
    }
}
