using System;
using System.Runtime.InteropServices;

namespace DanteMicBridge.Core
{
    /// <summary>
    /// P/Invoke definitions for Audinate Dante Application Library (DAL).
    /// DAL is distributed as a native C/C++ static or dynamic library (e.g. dal.dll / libdal.so)
    /// </summary>
    public static class DanteNative
    {
        private const string DAL_DLL = "dal.dll";

        public enum DalResult : int
        {
            DAL_SUCCESS = 0,
            DAL_ERROR_NOT_INITIALISED = 1,
            DAL_ERROR_INVALID_PARAM = 2,
            DAL_ERROR_NO_MEMORY = 3,
            DAL_ERROR_LICENSE_FAIL = 4,
            DAL_ERROR_NETWORK = 5,
            DAL_ERROR_NOT_SUPPORTED = 6
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DalConfig
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string device_name;
            public uint sample_rate;
            public uint tx_channels;
            public uint rx_channels;
            public uint latency_us;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string license_token;
        }

        // Delegate for DAL log callback
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void DalLogCallback(int level, [MarshalAs(UnmanagedType.LPStr)] string message);

        // Native entry points in official Audinate DAL
        [DllImport(DAL_DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern DalResult dal_init(ref DalConfig config, DalLogCallback? logCb);

        [DllImport(DAL_DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern DalResult dal_tx_send_float(uint channel, IntPtr samples, uint num_samples);

        [DllImport(DAL_DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern DalResult dal_tx_send_pcm24(uint channel, IntPtr samples, uint num_samples);

        [DllImport(DAL_DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern DalResult dal_shutdown();
    }
}
