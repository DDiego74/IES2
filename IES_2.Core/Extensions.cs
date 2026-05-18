using System;

namespace IES_2
{
    /// <summary>
    /// Extension methods shared between the Windows and Android projects.
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Limit value to a certain min/max interval.
        /// </summary>
        public static decimal Limit(this decimal value, decimal minValue, decimal maxValue)
        {
            return Math.Max(minValue, Math.Min(maxValue, value));
        }

        /// <summary>
        /// Get bit flag from a byte value.
        /// </summary>
        public static bool GetBit(this byte value, byte index)
        {
            return ((value & (byte)(1 << index)) != 0);
        }
    }
}
