/*
 * EnergyDrv keyboard backlight protocol. Adapted from protocol tables in:
 * https://github.com/SaltwaterC/KeyboardBacklightForLenovo (MIT; Stefan Rusu)
 * https://github.com/r00bertos1/lenovo-backlight-controller (MIT; Robert Makrocki)
 * See NOTICE for copyright and license attributions.
 * Pure mapping functions; this class has NO hardware access.
 */
using System;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class EnergyDrvProtocol
    {
        internal const uint Ioctl = 0x83102144u;
        internal const uint GetCommand = 0x00000032u;

        // Capability-gated, undocumented EnergyDrv alternative (IdeaNotebook).
        // Guard formula derived independently from protocol interoperability analysis.
        internal const uint GuardedCapabilityCommand = 0x00000001u;

        internal static bool TryGuardedCapability(uint raw, out uint guard)
        {
            guard = 0;
            // Only 3-state (Off, Low, High) capability is supported by our UI.
            // Other or unrecognized responses MUST NOT enable writes.
            if (raw != 0x00000005u) return false;
            guard = (raw & ~1u) << 3;
            return true;
        }

        internal static uint GuardedStatusCommand(uint guard)
        {
            if (guard != 0x20u) throw new ArgumentOutOfRangeException("guard");
            return guard | 0x02u;
        }

        internal static uint GuardedSetCommand(uint guard, int level)
        {
            if (guard != 0x20u) throw new ArgumentOutOfRangeException("guard");
            if (level < 0 || level > 2) throw new ArgumentOutOfRangeException("level");
            return guard | ((uint)level << 16) | 0x03u;
        }

        internal static bool TryDecodeGuardedStatus(uint raw, out int level)
        {
            level = -1;
            // Exact known 3-state replies; never accept Auto or unknown flags.
            if (raw != 1u && raw != 3u && raw != 5u) return false;
            level = (int)((raw >> 1) & 3u);
            return true;
        }

        // GET states are firmware-specific. Do not guess unknown values.
        internal static bool TryDecode(uint raw, out int level, out string variant)
        {
            variant = null;
            level = -1;
            switch (raw)
            {
                case 0x00000001u: variant = "v1"; level = 0; return true;
                case 0x00000003u: variant = "v1"; level = 1; return true;
                case 0x00000005u: variant = "v1"; level = 2; return true;
                case 0x00010001u: variant = "v2"; level = 0; return true;
                case 0x00010003u: variant = "v2"; level = 1; return true;
                case 0x00010005u: variant = "v2"; level = 2; return true;
                // v2 automatic mode 0x00010007 intentionally unsupported.
                default: return false;
            }
        }

        internal static uint SetCommand(int level)
        {
            if (level < 0 || level > 2)
                throw new ArgumentOutOfRangeException("level", "EnergyDrv supports Off, Low and High only.");
            return ((uint)level << 16) | 0x33u;
        }
    }
}
