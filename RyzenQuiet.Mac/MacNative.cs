using System;
using System.Runtime.InteropServices;
using System.Text;

namespace RyzenQuiet.Mac
{
    internal static class MacNative
    {
        private const string LibSystem = "libSystem.dylib";

        public const int PROCESSOR_CPU_LOAD_INFO = 2;
        public const int CPU_STATE_USER = 0;
        public const int CPU_STATE_SYSTEM = 1;
        public const int CPU_STATE_IDLE = 2;
        public const int CPU_STATE_NICE = 3;
        public const int CPU_STATE_MAX = 4;

        public const int HOST_VM_INFO64 = 4;
        public const int HOST_VM_INFO64_COUNT = 38;

        [DllImport(LibSystem, SetLastError = true)]
        public static extern int sysctlbyname(
            string name,
            IntPtr oldp,
            ref nuint oldlenp,
            IntPtr newp,
            nuint newlen);

        [DllImport(LibSystem)]
        public static extern IntPtr mach_host_self();

        [DllImport(LibSystem)]
        public static extern IntPtr mach_task_self();

        [DllImport(LibSystem)]
        public static extern int host_processor_info(
            IntPtr host,
            int flavor,
            out uint out_processor_count,
            out IntPtr out_processor_info,
            out uint out_processor_info_count);

        [DllImport(LibSystem)]
        public static extern int vm_deallocate(
            IntPtr target_task,
            IntPtr address,
            nuint size);

        [DllImport(LibSystem)]
        public static extern int host_statistics64(
            IntPtr host_priv,
            int flavor,
            IntPtr host_info_out,
            ref uint host_info_outCnt);

        public static string GetSysctlString(string name, string fallback = "")
        {
            try
            {
                nuint len = 0;
                if (sysctlbyname(name, IntPtr.Zero, ref len, IntPtr.Zero, 0) != 0 || len == 0)
                {
                    return fallback;
                }

                IntPtr buf = Marshal.AllocHGlobal((int)len);
                try
                {
                    if (sysctlbyname(name, buf, ref len, IntPtr.Zero, 0) == 0)
                    {
                        return Marshal.PtrToStringAnsi(buf)?.Trim() ?? fallback;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buf);
                }
            }
            catch { }
            return fallback;
        }

        public static long GetSysctlInt64(string name, long fallback = 0)
        {
            try
            {
                nuint len = (nuint)sizeof(long);
                IntPtr buf = Marshal.AllocHGlobal(sizeof(long));
                try
                {
                    if (sysctlbyname(name, buf, ref len, IntPtr.Zero, 0) == 0)
                    {
                        return Marshal.ReadInt64(buf);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buf);
                }
            }
            catch { }
            return fallback;
        }

        public static int GetSysctlInt32(string name, int fallback = 0)
        {
            try
            {
                nuint len = (nuint)sizeof(int);
                IntPtr buf = Marshal.AllocHGlobal(sizeof(int));
                try
                {
                    if (sysctlbyname(name, buf, ref len, IntPtr.Zero, 0) == 0)
                    {
                        return Marshal.ReadInt32(buf);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buf);
                }
            }
            catch { }
            return fallback;
        }
    }
}
