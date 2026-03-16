using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IntLimiter.Native;

#if WINDOWS7_0_OR_GREATER
public static class WfpNative
{
    public const uint RPC_C_AUTHN_WINNT = 10;

    [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
    public static extern uint FwpmEngineOpen0(
        string? serverName,
        uint authnService,
        IntPtr authIdentity,
        IntPtr session,
        out IntPtr engineHandle);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmEngineClose0(IntPtr engineHandle);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmFilterDeleteById0(IntPtr engineHandle, ulong id);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct FWPM_DISPLAY_DATA0
    {
        public IntPtr name;        // wchar_t*
        public IntPtr description; // wchar_t*
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FWP_BYTE_BLOB
    {
        public uint size;
        public IntPtr data; // UINT8*
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FWPM_SUBLAYER0
    {
        public Guid subLayerKey;
        public FWPM_DISPLAY_DATA0 displayData;
        public uint flags;
        public IntPtr providerKey; // GUID*
        public FWP_BYTE_BLOB providerData;
        public ushort weight;
    }

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmSubLayerAdd0(IntPtr engineHandle, ref FWPM_SUBLAYER0 subLayer, IntPtr sd);

    public static void EnsureSubLayer(IntPtr engine, Guid subLayerGuid)
    {
        try
        {
            var sl = new FWPM_SUBLAYER0
            {
                subLayerKey = subLayerGuid,
                weight = 0x100
            };
            uint hr = FwpmSubLayerAdd0(engine, ref sl, IntPtr.Zero);
            // 0x80320009 = FWP_E_ALREADY_EXISTS — expected on second run, safe to ignore.
            if (hr != 0 && hr != 0x80320009)
                Debug.WriteLine($"WFP FwpmSubLayerAdd0 returned 0x{hr:X8}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WFP EnsureSubLayer unexpected error: {ex.Message}");
        }
    }
}
#endif
