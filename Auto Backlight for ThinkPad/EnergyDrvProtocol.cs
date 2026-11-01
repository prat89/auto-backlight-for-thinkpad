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
