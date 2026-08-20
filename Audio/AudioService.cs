using System;
using System.Collections.Generic;
using Concentus.Enums;
using Concentus.Structs;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GW2ProximityChat
{
    /// <summary>
    /// Owns mic capture -> Opus encode and Opus decode -> mixed playback. Runs entirely on
    /// NAudio's *Event variants, which pump their own callback thread rather than relying on
    /// the host process's message loop, so this is safe to host inside Blish HUD.
    /// </summary>
    public class AudioService : IDisposable
    {
        public const int SampleRate = 48000;
        public const int FrameSamples = 960; // 20ms @ 48kHz mono
        private const int FrameBytes = FrameSamples * 2; // 16-bit PCM

        private readonly OpusEncoder _encoder;
        private readonly Dictionary<string, PeerAudioTrack> _peerTracks = new Dictionary<string, PeerAudioTrack>();
        private readonly MixingSampleProvider _mixer;
        private readonly VolumeSampleProvider _outputVolumeProvider;

        private readonly byte[] _captureAccumulator = new byte[FrameBytes];
        private int _captureAccumulatorLength;
        private readonly short[] _encodePcm = new short[FrameSamples];
        private readonly byte[] _encodeScratch = new byte[4000];

        private WaveInEvent _waveIn;
        private WaveOutEvent _waveOut;

        private float _outputVolume = 1f;
        private float _inputVolume = 1f;

        public bool MicrophoneEnabled { get; set; }
        public MicActivationMode ActivationMode { get; set; } = MicActivationMode.PushToTalk;

        /// <summary>Set every tick from the Push-to-Talk keybinding's held state.</summary>
        public bool PushToTalkActive { get; set; }

        /// <summary>0..1-ish RMS of captured audio (post <see cref="InputVolume"/>), updated
        /// continuously regardless of <see cref="MicrophoneEnabled"/> so a mic test/level meter
        /// works even while not transmitting.</summary>
        public float CurrentInputLevel { get; private set; }

        /// <summary>Amplitude threshold (same scale as <see cref="CurrentInputLevel"/>) above
        /// which Voice Activity mode transmits.</summary>
        public float NoiseGateThreshold { get; set; } = 0.02f;

        public float InputVolume
        {
            get => _inputVolume;
            set => _inputVolume = value;
        }

        public float OutputVolume
        {
            get => _outputVolume;
            set
            {
                _outputVolume = value;
                if (!OutputMuted) _outputVolumeProvider.Volume = value;
            }
        }

        private bool _outputMuted;

        /// <summary>Independent of <see cref="OutputVolume"/> -- muting and unmuting restores
        /// whatever volume was set, rather than needing to remember/re-enter it.</summary>
        public bool OutputMuted
        {
            get => _outputMuted;
            set
            {
                _outputMuted = value;
                _outputVolumeProvider.Volume = value ? 0f : _outputVolume;
            }
        }

        /// <summary>Fires with a freshly-allocated, exactly-sized Opus frame ready to send.</summary>
        public event Action<byte[]> EncodedFrameReady;

        public AudioService()
        {
            _encoder = new OpusEncoder(SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP)
            {
                Bitrate = 24000,
            };

            _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1))
            {
                ReadFully = true,
            };
            _outputVolumeProvider = new VolumeSampleProvider(_mixer) { Volume = _outputVolume };
        }

        /// <summary>Safe to call again later (e.g. the user picked a different device) --
        /// tears down any existing capture/playback before starting the new one.</summary>
        public void Start(int inputDeviceNumber, int outputDeviceNumber)
        {
            StopCaptureAndPlayback();

            _waveOut = new WaveOutEvent { DeviceNumber = outputDeviceNumber };
            _waveOut.Init(_outputVolumeProvider);
            _waveOut.Play();

            _waveIn = new WaveInEvent
            {
                DeviceNumber = inputDeviceNumber,
                WaveFormat = new WaveFormat(SampleRate, 16, 1),
                BufferMilliseconds = 20,
            };
            _waveIn.DataAvailable += OnCaptureDataAvailable;
            _waveIn.StartRecording();

            _captureAccumulatorLength = 0;
        }

        private void StopCaptureAndPlayback()
        {
            if (_waveIn != null)
            {
                _waveIn.DataAvailable -= OnCaptureDataAvailable;
                _waveIn.StopRecording();
                _waveIn.Dispose();
                _waveIn = null;
            }

            _waveOut?.Stop();
            _waveOut?.Dispose();
            _waveOut = null;
        }

        private void OnCaptureDataAvailable(object sender, WaveInEventArgs e)
        {
            int srcOffset = 0;
            while (srcOffset < e.BytesRecorded)
            {
                int toCopy = Math.Min(FrameBytes - _captureAccumulatorLength, e.BytesRecorded - srcOffset);
                Buffer.BlockCopy(e.Buffer, srcOffset, _captureAccumulator, _captureAccumulatorLength, toCopy);
                _captureAccumulatorLength += toCopy;
                srcOffset += toCopy;

                if (_captureAccumulatorLength == FrameBytes)
                {
                    ProcessCapturedFrame();
                    _captureAccumulatorLength = 0;
                }
            }
        }

        private void ProcessCapturedFrame()
        {
            ApplyVolume(_captureAccumulator, FrameBytes, _inputVolume);

            // Always measured, regardless of MicrophoneEnabled, so the level meter/gate
            // threshold can be calibrated (a "mic test") without actually transmitting.
            CurrentInputLevel = ComputeRms(_captureAccumulator, FrameBytes);

            if (!MicrophoneEnabled) return;

            bool shouldTransmit = ActivationMode == MicActivationMode.PushToTalk
                ? PushToTalkActive
                : CurrentInputLevel >= NoiseGateThreshold;

            if (!shouldTransmit) return;

            EncodeAndEmit();
        }

        private static void ApplyVolume(byte[] buffer, int count, float volume)
        {
            if (Math.Abs(volume - 1f) < 0.001f) return;

            for (int i = 0; i + 1 < count; i += 2)
            {
                short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
                int boosted = (int)(sample * volume);
                boosted = Math.Max(short.MinValue, Math.Min(short.MaxValue, boosted));
                buffer[i] = (byte)(boosted & 0xFF);
                buffer[i + 1] = (byte)((boosted >> 8) & 0xFF);
            }
        }

        private static float ComputeRms(byte[] buffer, int count)
        {
            int sampleCount = count / 2;
            if (sampleCount == 0) return 0f;

            double sumSquares = 0;
            for (int i = 0; i + 1 < count; i += 2)
            {
                short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
                sumSquares += (double)sample * sample;
            }

            double rms = Math.Sqrt(sumSquares / sampleCount);
            return (float)(rms / short.MaxValue);
        }

        private void EncodeAndEmit()
        {
            Buffer.BlockCopy(_captureAccumulator, 0, _encodePcm, 0, FrameBytes);

            int length;
            try
            {
                length = _encoder.Encode(_encodePcm, 0, FrameSamples, _encodeScratch, 0, _encodeScratch.Length);
            }
            catch
            {
                return;
            }

            var frame = new byte[length];
            Buffer.BlockCopy(_encodeScratch, 0, frame, 0, length);
            EncodedFrameReady?.Invoke(frame);
        }

        public void SubmitPeerAudio(string peerId, byte[] opusPayload)
        {
            PeerAudioTrack track;
            lock (_peerTracks)
            {
                if (!_peerTracks.TryGetValue(peerId, out track))
                {
                    track = new PeerAudioTrack();
                    _peerTracks[peerId] = track;
                    _mixer.AddMixerInput(track);
                }
            }

            track.EnqueueOpusPacket(opusPayload);
        }

        public void SetPeerGain(string peerId, float gain)
        {
            lock (_peerTracks)
            {
                if (_peerTracks.TryGetValue(peerId, out var track))
                {
                    track.Gain = gain;
                }
            }
        }

        /// <summary>Drops any peer track not present in the current roster (left instance/disconnected).</summary>
        public void RetainOnly(HashSet<string> activePeerIds)
        {
            lock (_peerTracks)
            {
                List<string> toRemove = null;
                foreach (var id in _peerTracks.Keys)
                {
                    if (!activePeerIds.Contains(id))
                    {
                        (toRemove ?? (toRemove = new List<string>())).Add(id);
                    }
                }

                if (toRemove == null) return;

                foreach (var id in toRemove)
                {
                    _mixer.RemoveMixerInput(_peerTracks[id]);
                    _peerTracks.Remove(id);
                }
            }
        }

        public void Dispose()
        {
            StopCaptureAndPlayback();
        }
    }
}
