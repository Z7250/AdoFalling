using System;
using System.Reflection;

namespace AdoFalling8K
{
    /// <summary>
    /// Drives the game's own auto-play flag (RDC.auto) so a level plays itself.
    ///
    /// This exists so the overlay's countdown timing can be checked without a human playing,
    /// and so a user can watch the falling overlay on a chart they cannot clear. It is the
    /// same switch the game's built-in auto-play button uses.
    /// </summary>
    internal sealed class AutoPlayDriver
    {
        private static MemberInfo _auto;
        private bool _applied;
        private bool _previousValue;

        public string Status = "off";

        /// <summary>Turn auto-play on or off to match the config.</summary>
        public void Apply(bool enable)
        {
            if (!enable)
            {
                if (_applied)
                {
                    if (!Set(_previousValue)) return;
                    _applied = false;
                    Status = "off";
                }
                return;
            }

            if (_auto == null) Probe();
            if (_auto == null) return;
            if (!_applied)
            {
                object value = GameReflection.Read(null, _auto);
                _previousValue = Convert.ToBoolean(value);
            }

            // Re-applied on every poll, deliberately: the game's RDC state is rebuilt when it
            // switches scenes, so setting it once at the title screen has no effect in a level.
            if (!Set(true)) return;
            _applied = true;
            Status = "on (RDC.auto)";
        }

        private static void Probe()
        {
            try
            {
                Assembly asm = GameReflection.Assembly;
                Type t = asm != null ? GameReflection.FindType(asm, "RDC") : null;
                if (t == null)
                {
                    return;
                }
                _auto = GameReflection.FindMember(t, "auto");
            }
            catch
            {
                _auto = null;
            }
        }

        private static bool Set(bool value)
        {
            if (_auto == null)
                return false;
            try
            {
                return GameReflection.Write(null, _auto, value);
            }
            catch (Exception ex)
            {
                FallingPlugin.Log.LogWarning("auto-play toggle failed: " + ex.Message);
            }
            return false;
        }

        public bool Available { get { return _auto != null; } }
    }
}
