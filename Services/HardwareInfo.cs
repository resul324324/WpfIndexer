using System;
using System.Runtime.InteropServices;

namespace WpfIndexer.Services
{
    /// <summary>
    /// Basit Windows-specific donanım bilgisi yardımcı sınıfı.
    /// Toplam fiziksel belleği alır ve Lucene için önerilen RAM buffer MB hesaplar.
    /// </summary>
    internal static class HardwareInfo
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        public static ulong GetTotalPhysicalMemoryBytes()
        {
            var memStatus = new MEMORYSTATUSEX();
            memStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (!GlobalMemoryStatusEx(ref memStatus))
            {
                return 0;
            }
            return memStatus.ullTotalPhys;
        }

        public static long GetTotalPhysicalMemoryMB()
        {
            var bytes = GetTotalPhysicalMemoryBytes();
            if (bytes == 0) return 0;
            return (long)(bytes / 1024 / 1024);
        }

        /// <summary>
        /// Compute a recommended RAMBufferSizeMB for Lucene based on total RAM.
        /// Rule:
        /// - candidate = totalMB / 32
        /// - min 16 MB, max 512 MB
        /// Rounded to 2 decimals.
        /// </summary>
        public static double ComputeLuceneRamBufferSizeMB()
        {
            long totalMB = GetTotalPhysicalMemoryMB();
            if (totalMB <= 0)
            {
                return 64.0;
            }

            double candidate = Math.Max(16.0, totalMB / 32.0);

            const double minMB = 16.0;
            const double maxMB = 512.0;

            if (candidate < minMB) candidate = minMB;
            if (candidate > maxMB) candidate = maxMB;

            return Math.Round(candidate, 2);
        }
    }
}