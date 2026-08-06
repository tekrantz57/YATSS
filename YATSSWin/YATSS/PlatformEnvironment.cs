using System.Runtime.InteropServices;

namespace YATSS
{
    internal static class PlatformEnvironment
    {
        private static readonly Lazy<bool> RunningUnderWine = new(DetectWine);

        public static bool IsWine => RunningUnderWine.Value;

        private static bool DetectWine()
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WINEPREFIX")))
            {
                return true;
            }

            IntPtr module = IntPtr.Zero;
            try
            {
                if (!NativeLibrary.TryLoad("ntdll.dll", out module))
                {
                    return false;
                }

                return NativeLibrary.TryGetExport(module, "wine_get_version", out _);
            }
            catch
            {
                return false;
            }
            finally
            {
                if (module != IntPtr.Zero)
                {
                    NativeLibrary.Free(module);
                }
            }
        }
    }
}

