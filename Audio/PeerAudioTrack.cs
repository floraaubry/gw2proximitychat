using System.Collections.Generic;
using Concentus.Structs;
using NAudio.Wave;

namespace GW2ProximityChat
{
    /// <summary>
    /// Per-peer Opus decode + jitter buffer, exposed as an NAudio ISampleProvider so it can be
    /// dropped straight into a MixingSampleProvider. Gain is applied per-sample on Read so it
    /// tracks the listener's live distance to this peer without re-decoding anything.
    /// </summary>
    public class PeerAudioTrack : ISampleProvider
    {
        public const int SampleRate = 48000;
        public const int FrameSamples = 960; // 20ms @ 48kHz mono

        // Cap how far behind the network the queue can get so a stalled connection doesn't
        // build up unbounded latency; oldest audio is dropped first.
        private const int MaxQueuedSamples = FrameSamples * 10;

        private readonly OpusDecoder _decoder = new OpusDecoder(SampleRate, 1);
        private readonly Queue<float> _sampleQueue = new Queue<float>();
        private readonly object _lock = new object();
        private readonly short[] _decodeBuffer = new short[FrameSamples];

        public float Gain { get; set; } = 1f;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);

        public void EnqueueOpusPacket(byte[] opus)
        {
            int decoded = _decoder.Decode(opus, 0, opus.Length, _decodeBuffer, 0, FrameSamples);

            lock (_lock)
            {
                for (int i = 0; i < decoded; i++)
                {
                    _sampleQueue.Enqueue(_decodeBuffer[i] / 32768f);
                }

                while (_sampleQueue.Count > MaxQueuedSamples)
                {
                    _sampleQueue.Dequeue();
                }
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            float gain = Gain;

            lock (_lock)
            {
                int i = 0;
                for (; i < count && _sampleQueue.Count > 0; i++)
                {
                    buffer[offset + i] = _sampleQueue.Dequeue() * gain;
                }

                for (; i < count; i++)
                {
                    buffer[offset + i] = 0f;
                }
            }

            return count;
        }
    }
}
