using System;
using N3Lite;

namespace LostEden.Vehicles
{
    /// <summary>
    /// The game's movement rules, turned into the plain numbers N3Lite takes: the per-state speed
    /// curves driven by the run-speed stat, the strafe rule, which states lock the drive, and the jump
    /// height from stats. See <c>N3Lite/docs/Movement.md</c>.
    ///
    /// <para>
    /// The state is the network's <c>MovementState</c> number. <b>The correspondence with stock's
    /// vehicle state is not fully recovered</b> — only that 7 is Fly and that 1, 8 and 9 refuse forward
    /// drive (<c>FUN_10070a2f</c>). The names behind the other numbers are not known, so it stays an
    /// <c>int</c>.
    /// </para>
    ///
    /// <para>Unity-free, so <c>Tests/Movement</c> links it directly.</para>
    /// </summary>
    public static class CharMovementRules
    {
        public const int StateRooted = 1;
        public const int StateWalk = 2;
        public const int StateRun = 3;
        public const int StateFly = 7;
        public const int StateSit = 8;

        /// <summary>
        /// The profile for <paramref name="state"/> at <paramref name="runSpeedStat"/>.
        ///
        /// <para>
        /// A player's vehicle refuses longitudinal steering in states 1, 8 and 9; an NPC's only in 1,
        /// where it halts instead. Sit (8) also drops input and refuses
        /// jumps, which is the dynel's rule rather than the vehicle's.
        /// </para>
        /// </summary>
        public static MovementProfile Profile(int state, float runSpeedStat, bool isNpc) => new MovementProfile
        {
            ForwardSpeed = MaxSpeed(state, 1, runSpeedStat),
            ReverseSpeed = MaxSpeed(state, 2, runSpeedStat),
            StrafeSpeed = StrafeSpeed(state, 1, runSpeedStat),
            ReverseStrafeSpeed = StrafeSpeed(state, 2, runSpeedStat),
            CanDrive = isNpc
                ? state != StateRooted
                : state != 9 && state != StateSit && state != StateRooted,
            AcceptsInput = state != StateSit,
            Flying = state == StateFly,
        };

        struct Curve
        {
            public float Divisor;
            public float Base;
            public float Max;
            public float Min;
            public bool Constant;
        }

        /// <summary>The per-state speed curve. <paramref name="direction"/> 2 is the reverse curve.</summary>
        static Curve CurveFor(int state, int direction)
        {
            switch (state)
            {
                case 3:
                    return direction == 2
                        ? new Curve { Divisor = 275f / 0.7f, Base = 3f, Max = 9.099999f, Min = 1.05f }
                        : new Curve { Divisor = 275f, Base = 5f, Max = 13f, Min = 1.5f };
                case 4:
                    return new Curve { Divisor = 275f / 0.625f, Base = 3f, Max = 8f, Min = 1.5f };
                case 7:
                    return new Curve { Divisor = 275f, Base = 7f, Max = 15f, Min = 1.5f };
                case 5:
                    return new Curve { Base = 1f, Constant = true };
                default:
                    // state 2 and everything unlisted
                    return new Curve { Base = 1.5f, Constant = true };
            }
        }

        /// <summary>
        /// The top speed: <c>stat / divisor + base</c> clamped to the curve's range, or the constant.
        /// The vehicle floors anything under 0.01 to 0.1 (<see cref="CharVehicleSim.UpdateMotionConstraints"/>).
        /// </summary>
        public static float MaxSpeed(int state, int direction, float runSpeedStat)
        {
            Curve curve = CurveFor(state, direction);
            if (curve.Constant)
                return curve.Base;

            float v = runSpeedStat / curve.Divisor + curve.Base;
            if (v > curve.Max)
                v = curve.Max;
            if (v < curve.Min)
                v = curve.Min;
            return v;
        }

        /// <summary>
        /// The strafe speed: the curve <b>scaled by 0.5</b>, with a floor of 0.75 and no separate
        /// maximum — <c>clamp(0.5*stat/divisor + 0.5*base, 0.75, 0.5*max)</c>. For the run state that
        /// is <c>clamp(stat*0.5/275 + 2.5, 0.75, 6.5)</c>.
        /// </summary>
        public static float StrafeSpeed(int state, int direction, float runSpeedStat)
        {
            const float Scale = 0.5f;
            const float Floor = 0.75f;

            if (state == StateWalk)
                return Math.Max(Floor, 1.5f);

            Curve curve = CurveFor(state, direction);
            if (curve.Constant)
                return Math.Max(Floor, curve.Base);

            float v = Scale * runSpeedStat / curve.Divisor + Scale * curve.Base;
            float max = Scale * curve.Max;
            if (v > max)
                v = max;
            if (v < Floor)
                v = Floor;
            return v;
        }

        /// <summary>
        /// A character's jump height from Strength (stat 16), Agility (17) and GmLevel (215):
        /// <c>(str + agi) / 200 + 1</c>, at least 0.5. Past 800 in total a non-GM counts as exactly
        /// 800 (<c>str = 800, agi = 0</c>).
        /// </summary>
        public static float JumpHeight(int strength, int agility, int gmLevel)
        {
            float str = strength;
            float agi = agility;
            if (800f < agi + str && gmLevel == 0)
            {
                str = 800f;
                agi = 0f;
            }

            float height = (float)((agi + str) / 200.0 + 1.0);
            if (height < 0.5f)
                height = 0.5f;
            return height;
        }

        /// <summary>The ceiling clamp keeps twice the body scale clear above the position.</summary>
        public static float BodyHeight(float bodyScale) => bodyScale + bodyScale;
    }
}
