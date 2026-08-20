using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace GW2ProximityChat
{
    /// <summary>
    /// P/Invoke wrapper around the bundled native rnnoise.dll (BSD-3-Clause, unmodified
    /// xiph/rnnoise -- see native/README.md) for real-time microphone noise suppression.
    /// rnnoise processes exactly <see cref="FrameSize"/> samples per call, as floats holding
    /// the FULL int16 magnitude (e.g. -32768f..32767f), NOT normalized -1..1 -- confirmed via
    /// the library's own examples/rnnoise_demo.c (`x[i] = (float)shortSample[i]`, no scaling).
    /// </summary>
    public class RnnoiseProcessor : IDisposable
    {
        public const int FrameSize = 480; // 10ms @ 48kHz -- fixed by the library, not configurable

        private static bool _nativeLibraryLoaded;

        private IntPtr _state;

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadLibrary(string dllToLoad);

        [DllImport("rnnoise", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr rnnoise_create(IntPtr model);

        [DllImport("rnnoise", CallingConvention = CallingConvention.Cdecl)]
        private static extern void rnnoise_destroy(IntPtr state);

        [DllImport("rnnoise", CallingConvention = CallingConvention.Cdecl)]
        private static extern float rnnoise_process_frame(IntPtr state, float[] outSamples, float[] inSamples);

        /// <summary>Loads native/rnnoise.dll from beside this assembly. Required because this
        /// is a Blish HUD module, not the host process (Blish HUD.exe) -- the default Win32
        /// DLL search order is based on the *host EXE's* directory, so a bare
        /// [DllImport("rnnoise")] would never find a DLL sitting next to this assembly on its
        /// own. Once loaded by full path, Windows resolves subsequent bare-name P/Invoke calls
        /// to the already-resident module regardless of search path (confirmed: a DLL already
        /// loaded in the process is matched by module name before any path search happens).
        /// Confirmed the bundling side of this by unzipping a built .bhm -- the whole build
        /// output directory (NAudio.dll etc. included) lands flat, GW2ProximityChat.dll's
        /// directory is exactly where rnnoise.dll ends up too.</summary>
        private static void EnsureNativeLibraryLoaded()
        {
            if (_nativeLibraryLoaded) return;

            string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            string dllPath = Path.Combine(assemblyDir, "rnnoise.dll");

            if (LoadLibrary(dllPath) == IntPtr.Zero)
            {
                throw new DllNotFoundException($"Could not load '{dllPath}' (Win32 error {Marshal.GetLastWin32Error()})");
            }

            _nativeLibraryLoaded = true;
        }

        public RnnoiseProcessor()
        {
            EnsureNativeLibraryLoaded();

            _state = rnnoise_create(IntPtr.Zero); // NULL model = the library's bundled default model
            if (_state == IntPtr.Zero) throw new InvalidOperationException("rnnoise_create returned NULL");
        }

        /// <summary>Denoises exactly <see cref="FrameSize"/> samples in place.</summary>
        public void ProcessFrame(float[] samples)
        {
            rnnoise_process_frame(_state, samples, samples);
        }

        public void Dispose()
        {
            if (_state == IntPtr.Zero) return;

            rnnoise_destroy(_state);
            _state = IntPtr.Zero;
        }
    }
}
