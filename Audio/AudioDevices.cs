using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace GW2ProximityChat
{
    public static class AudioDevices
    {
        public const int SystemDefaultIndex = -1;
        public const string SystemDefaultLabel = "System Default";

        public static List<(int Index, string Label)> GetInputDevices()
        {
            return GetDevices(DataFlow.Capture, WaveInEvent.DeviceCount, i => WaveInEvent.GetCapabilities(i).ProductName);
        }

        public static List<(int Index, string Label)> GetOutputDevices()
        {
            return GetDevices(DataFlow.Render, WaveOut.DeviceCount, i => WaveOut.GetCapabilities(i).ProductName);
        }

        private static List<(int Index, string Label)> GetDevices(DataFlow flow, int count, Func<int, string> getWinMmName)
        {
            var devices = new List<(int Index, string Label)> { (SystemDefaultIndex, SystemDefaultLabel) };
            var fullNames = TryGetWasapiFriendlyNames(flow);

            for (int i = 0; i < count; i++)
            {
                string truncated = getWinMmName(i).TrimEnd();
                string label = ResolveFullName(truncated, fullNames) ?? truncated;
                devices.Add((i, $"{label} [{i}]"));
            }

            return devices;
        }

        // The legacy WinMM device-caps struct WaveInEvent/WaveOut use for enumeration (needed
        // because their DeviceNumber is a WinMM index, not a WASAPI endpoint id) hard-truncates
        // names to 31 characters. The modern WASAPI endpoint list doesn't truncate, and the
        // truncated name is always a literal prefix of the real one, so matching on StartsWith
        // recovers the full name -- but only where exactly one WASAPI device matches, since a
        // truncated prefix could in principle collide across two similarly-named devices.
        private static string ResolveFullName(string truncated, List<string> fullNames)
        {
            if (fullNames == null) return null;

            var matches = fullNames.Where(n => n.StartsWith(truncated, StringComparison.Ordinal)).ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        private static List<string> TryGetWasapiFriendlyNames(DataFlow flow)
        {
            try
            {
                var enumerator = new MMDeviceEnumerator();
                return enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active)
                    .Select(d => d.FriendlyName)
                    .ToList();
            }
            catch
            {
                // No WASAPI endpoint enumeration available for some reason -- callers fall back
                // to the (possibly truncated) WinMM name instead.
                return null;
            }
        }
    }
}
