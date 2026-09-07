using System;
using System.Collections.Generic;
using System.Linq;
using Croquet.Core;
using Xunit;

namespace Croquet.Core.Tests
{
    /// <summary>
    /// The groundwork for playing against somebody who is not in the room.
    ///
    /// Nothing here talks to a network. What it establishes is the property
    /// online play rests on: that a setup plus an ordered list of strokes IS
    /// the game, so two ends that agree about those agree about everything, and
    /// any disagreement shows up as one number differing on the stroke it
    /// happened rather than as an argument three turns later.
    /// </summary>
    public class MatchTests
    {
        /// <summary>
        /// A court built here rather than taken from the defaults, so a tuning
        /// pass never turns this suite red.
        /// </summary>
        static CourtSpec Lawn() => new CourtSpec
        {
            Width = 30,
            Height = 15,
            Friction = 0.9,
            Restitution = 0.8,
            ObstacleRestitution = 0.5
        };

        static MatchSetup Setup(int balls = 4, int teams = 0) => new MatchSetup
        {
            Variant = Variant.NineWicket,
            Balls = balls,
            Teams = teams,
            Court = Lawn()
        };

        /// <summary>
        /// The reference rally, played on a court of this test's choosing. It
        /// lives in core rather than here so a Unity build can play the very
        /// same one and be compared against it -- which is the whole point of
        /// pinning a number at all.
        /// </summary>
        static Match PlayRally(MatchSetup setup)
        {
            var m = Match.Begin(setup);
            foreach (var s in Reference.Strokes())
            {
                if (m.Game.Winner != null) break;

                var fitting = s.IsBonus == (m.Game.Stroke == StrokeKind.Bonus)
                    ? s
                    : Stroke.Bonus(BonusWay.CroquetShot, new Vec2(-1, 0), s.Aim, s.Power);

                m.Play(fitting);
            }
            return m;
        }

        // ---- a stroke is a value ------------------------------------------

        [Fact]
        public void A_stroke_survives_being_written_down()
        {
            var a = Stroke.Bonus(BonusWay.MalletHead, new Vec2(-1, 0.5), new Vec2(1, 0.25), 4.5);
            var b = Stroke.Bonus(BonusWay.MalletHead, new Vec2(-1, 0.5), new Vec2(1, 0.25), 4.5);

            Assert.Equal(a, b);
            Assert.Equal(Checksum.Of(a), Checksum.Of(b));
        }

        [Fact]
        public void Strokes_that_differ_at_all_are_not_equal()
        {
            var baseline = Stroke.Ordinary(new Vec2(1, 0), 4.0);

            Assert.NotEqual(baseline, Stroke.Ordinary(new Vec2(1, 0), 4.0000001));
            Assert.NotEqual(baseline, Stroke.Ordinary(new Vec2(1, 0.0000001), 4.0));
            Assert.NotEqual(baseline, Stroke.Bonus(BonusWay.CroquetShot, new Vec2(-1, 0),
                                                   new Vec2(1, 0), 4.0));
        }

        [Fact]
        public void A_stroke_of_the_wrong_shape_is_refused_rather_than_thrown_inside_the_rules()
        {
            var m = Match.Begin(Setup());

            // A bonus stroke when none is owed.
            var wrong = Stroke.Bonus(BonusWay.CroquetShot, new Vec2(-1, 0), new Vec2(1, 0), 3);
            Assert.False(wrong.Fits(m.Game));
            Assert.Throws<InvalidOperationException>(() => m.Play(wrong));

            // And nothing was recorded by the attempt.
            Assert.Empty(m.Strokes);

            Assert.False(Stroke.Ordinary(Vec2.Zero, 3).Fits(m.Game));        // no direction
            Assert.False(Stroke.Ordinary(new Vec2(1, 0), 0).Fits(m.Game));   // no strength
            Assert.False(Stroke.Ordinary(new Vec2(1, 0), double.NaN).Fits(m.Game));
        }

        // ---- the log is the game ------------------------------------------

        [Fact]
        public void Replaying_the_strokes_reproduces_the_game_exactly()
        {
            // The property the whole idea rests on: hand somebody the setup and
            // the strokes and they arrive where you are. This is the save file,
            // the spectator feed and the rejoining client, all at once.
            var played = PlayRally(Setup());
            var again = Match.Rebuild(Setup(), played.Strokes);

            Assert.Equal(played.Count, again.Count);
            Assert.Equal(played.Hash, again.Hash);
            Assert.Equal(played.Checkpoints, again.Checkpoints);

            for (int i = 0; i < played.Game.World.Balls.Length; i++)
            {
                Assert.Equal(played.Game.World.Balls[i].Pos, again.Game.World.Balls[i].Pos);
                Assert.Equal(played.Game.States[i].Point, again.Game.States[i].Point);
            }
        }

        [Fact]
        public void Every_stroke_is_recorded_once_and_in_order()
        {
            var m = PlayRally(Setup());

            Assert.True(m.Count > 0, "the rally should have played something");
            Assert.Equal(m.Count, m.Checkpoints.Count);
            Assert.Equal(m.Checkpoints[m.Count - 1], m.Hash);
        }

        // ---- disagreement is visible --------------------------------------

        [Fact]
        public void One_different_stroke_shows_up_as_a_different_checksum()
        {
            var mine = PlayRally(Setup());

            // The same rally with the very first stroke a hair off line. A
            // divergence that small is exactly the kind that would otherwise go
            // unnoticed until the two ends were visibly playing different games.
            var theirs = Match.Begin(Setup());
            var altered = mine.Strokes.ToArray();
            altered[0] = Stroke.Ordinary(new Vec2(1, 0.0500001), 4.2);
            foreach (var s in altered)
            {
                if (theirs.Game.Winner != null) break;
                if (!s.Fits(theirs.Game)) break;
                theirs.Play(s);
            }

            Assert.NotEqual(mine.Checkpoints[0], theirs.Checkpoints[0]);
            Assert.False(mine.Agrees(0, theirs.Checkpoints[0]));
        }

        [Fact]
        public void A_court_tuned_differently_is_a_different_match()
        {
            // Feel is part of what the two ends agree on. The same strokes on a
            // keener lawn land somewhere else, so the setup carries the court
            // rather than rebuilding it from a default that might have moved.
            var keen = Setup();
            keen.Court.Friction = 0.6;

            var heavy = Setup();
            heavy.Court.Friction = 1.4;

            Assert.NotEqual(keen.Hash, heavy.Hash);
            Assert.NotEqual(PlayRally(keen).Hash, PlayRally(heavy).Hash);
        }

        [Fact]
        public void The_checksum_notices_every_part_of_the_state()
        {
            var m = Match.Begin(Setup());
            ulong before = Checksum.Of(m.Game);

            m.Game.States[0].Dead.Add(2);
            Assert.NotEqual(before, Checksum.Of(m.Game));

            m.Game.States[0].Dead.Remove(2);
            Assert.Equal(before, Checksum.Of(m.Game));

            m.Game.World.Balls[1].Pos = new Vec2(m.Game.World.Balls[1].Pos.X + 1e-12,
                                                 m.Game.World.Balls[1].Pos.Y);
            Assert.NotEqual(before, Checksum.Of(m.Game));
        }

        [Fact]
        public void Deadness_hashes_the_same_whatever_order_it_was_added_in()
        {
            // A HashSet has no order, and two clients that agree about the game
            // could otherwise disagree about the number. A false alarm here
            // would be worse than no alarm at all.
            var a = Match.Begin(Setup());
            var b = Match.Begin(Setup());

            foreach (var d in new[] { 3, 1, 2 }) a.Game.States[0].Dead.Add(d);
            foreach (var d in new[] { 2, 3, 1 }) b.Game.States[0].Dead.Add(d);

            Assert.Equal(Checksum.Of(a.Game), Checksum.Of(b.Game));
        }

        // ---- the setup ----------------------------------------------------

        [Fact]
        public void Association_croquet_settles_its_own_balls_and_sides()
        {
            var m = Match.Begin(new MatchSetup
            {
                Variant = Variant.SixWicket,
                Balls = 6,          // asked for, and not what association croquet is
                Teams = 0,
                Court = Lawn()
            });

            Assert.Equal(4, m.Game.World.Balls.Length);
            Assert.NotNull(m.Game.Side);
            Assert.Equal(2, m.Game.Side.Distinct().Count());
        }

        [Fact]
        public void A_split_that_does_not_divide_the_balls_is_no_split()
        {
            var m = Match.Begin(Setup(balls: 5, teams: 2));
            Assert.Null(m.Game.Side);
        }

        [Fact]
        public void The_setup_is_copied_so_the_caller_cannot_change_it_underneath()
        {
            var setup = Setup();
            var m = Match.Begin(setup);

            setup.Court.Friction = 99;
            setup.Balls = 2;

            Assert.NotEqual(99, m.Game.World.Spec.Friction);
            Assert.Equal(4, m.Game.World.Balls.Length);
        }

        // ---- determinism, which is the thing that has to hold --------------

        [Fact]
        public void The_same_match_played_twice_lands_on_the_same_number()
        {
            Assert.Equal(PlayRally(Setup()).Hash, PlayRally(Setup()).Hash);
        }

        [Fact]
        public void A_known_rally_lands_on_a_pinned_checksum()
        {
            // The cross-platform canary.
            //
            // The tests run on CoreCLR and the game runs on Mono or IL2CPP, and
            // nothing has ever checked that those agree. If they do not, online
            // play built on sending strokes is built on sand -- so this pins a
            // number, and the same number can be printed on a device and
            // compared by eye.
            //
            // It failing does NOT necessarily mean something is broken: any
            // deliberate change to the simulation moves it, and the fix is then
            // to re-pin. What it must never do is move on its own.
            Assert.True(Reference.Agrees(out var got),
                        $"this build lands on 0x{got:X16}, not the pinned 0x{Reference.Hash:X16}");

            Assert.Equal(Reference.Length, Reference.Play().Count);
        }
    }
}
