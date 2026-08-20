using System;
using System.Collections.Generic;
using Blish_HUD;
using Concentus.Enums;
using Concentus.Structs;
using Microsoft.Xna.Framework;
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
        private static readonly Logger Logger = Logger.GetLogger<AudioService>();

        public const int SampleRate = 48000;
        public const int FrameSamples = 960; // 20ms @ 48kHz mono
        private const int FrameBytes = FrameSamples * 2; // 16-bit PCM

        private readonly OpusEncoder _encoder;
        private readonly Dictionary<string, PeerMixEntry> _peerTracks = new Dictionary<string, PeerMixEntry>();
        private readonly MixingSampleProvider _mixer;
        private readonly VolumeSampleProvider _outputVolumeProvider;

        /// <summary>Pairs a peer's decode/gain track with the stereo wrapper that gives it a
        /// left/right pan -- the mixer only accepts stereo inputs (matching its own
        /// WaveFormat), and MonoToStereoSampleProvider's LeftVolume/RightVolume is what
        /// actually places the peer in the stereo field.</summary>
        private class PeerMixEntry
        {
            public PeerAudioTrack Track;
            public MonoToStereoSampleProvider Stereo;
        }

        /// <summary>Restarts the wrapped <see cref="AudioFileReader"/> from the beginning
        /// whenever it runs dry, so a test file loops instead of going silent partway
        /// through the Debug tab's range check.</summary>
        private class LoopingSampleProvider : ISampleProvider
        {
            private readonly AudioFileReader _reader;
            private readonly ISampleProvider _source;

            public LoopingSampleProvider(AudioFileReader reader, ISampleProvider source)
            {
                _reader = reader;
                _source = source;
            }

            public WaveFormat WaveFormat => _source.WaveFormat;

            public int Read(float[] buffer, int offset, int count)
            {
                int total = 0;
                int consecutiveEmptyReads = 0;

                while (total < count && consecutiveEmptyReads < 2)
                {
                    int read = _source.Read(buffer, offset + total, count - total);
                    if (read == 0)
                    {
                        consecutiveEmptyReads++;
                        _reader.Position = 0;
                        continue;
                    }

                    consecutiveEmptyReads = 0;
                    total += read;
                }

                return total;
            }
        }

        private readonly byte[] _captureAccumulator = new byte[FrameBytes];
        private int _captureAccumulatorLength;
        private readonly short[] _encodePcm = new short[FrameSamples];
        private readonly byte[] _encodeScratch = new byte[4000];

        // FrameSamples (960, 20ms) is 2x RnnoiseProcessor.FrameSize (480, 10ms) -- rnnoise
        // runs twice per captured frame. Null (and Denoise() a no-op) if the native library
        // failed to load.
        private readonly RnnoiseProcessor _noiseSuppressor;
        private readonly float[] _denoiseScratch = new float[RnnoiseProcessor.FrameSize];

        // Debug-tab loopback test: plays a local audio file through the same mixer/pan path
        // real peers use (bypassing Opus/network entirely) so gain/pan can be checked by ear
        // without a second player. Null when nothing is loaded.
        private AudioFileReader _testFileReader;
        private MonoToStereoSampleProvider _testFileStereo;
        private float _testFileGain = 1f;
        private float _testFilePan;

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

        /// <summary>On by default; silently becomes a no-op (rather than failing module load)
        /// if the bundled native rnnoise.dll couldn't be loaded -- see RnnoiseProcessor.</summary>
        public bool NoiseSuppressionEnabled { get; set; } = true;

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

            _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2))
            {
                ReadFully = true,
            };
            _outputVolumeProvider = new VolumeSampleProvider(_mixer) { Volume = _outputVolume };

            try
            {
                _noiseSuppressor = new RnnoiseProcessor();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Noise suppression unavailable (failed to load rnnoise.dll) -- continuing without it");
                _noiseSuppressor = null;
            }
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
            if (NoiseSuppressionEnabled && _noiseSuppressor != null) Denoise(_captureAccumulator);

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

        /// <summary>Runs rnnoise over the captured frame in place, FrameSize (480) samples at
        /// a time -- twice, since a captured frame is 960 samples. int16 PCM bytes <->
        /// full-range floats (rnnoise's expected scale, not normalized -1..1) each way.</summary>
        private void Denoise(byte[] buffer)
        {
            for (int chunkStart = 0; chunkStart < FrameSamples; chunkStart += RnnoiseProcessor.FrameSize)
            {
                for (int i = 0; i < RnnoiseProcessor.FrameSize; i++)
                {
                    int byteOffset = (chunkStart + i) * 2;
                    short sample = (short)(buffer[byteOffset] | (buffer[byteOffset + 1] << 8));
                    _denoiseScratch[i] = sample;
                }

                _noiseSuppressor.ProcessFrame(_denoiseScratch);

                for (int i = 0; i < RnnoiseProcessor.FrameSize; i++)
                {
                    int byteOffset = (chunkStart + i) * 2;
                    short sample = (short)MathHelper.Clamp(_denoiseScratch[i], short.MinValue, short.MaxValue);
                    buffer[byteOffset] = (byte)(sample & 0xFF);
                    buffer[byteOffset + 1] = (byte)((sample >> 8) & 0xFF);
                }
            }
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
            PeerMixEntry entry;
            lock (_peerTracks)
            {
                if (!_peerTracks.TryGetValue(peerId, out entry))
                {
                    var track = new PeerAudioTrack();
                    var stereo = new MonoToStereoSampleProvider(track) { LeftVolume = 1f, RightVolume = 1f };
                    entry = new PeerMixEntry { Track = track, Stereo = stereo };
                    _peerTracks[peerId] = entry;
                    _mixer.AddMixerInput(stereo);
                }
            }

            entry.Track.EnqueueOpusPacket(opusPayload);
        }

        public void SetPeerGain(string peerId, float gain)
        {
            lock (_peerTracks)
            {
                if (_peerTracks.TryGetValue(peerId, out var entry))
                {
                    entry.Track.Gain = gain;
                }
            }
        }

        /// <summary>-1 (full left) .. 0 (center) .. 1 (full right), same convention as
        /// <see cref="GainCalculator.ComputePan"/>. Simple linear balance rather than an
        /// equal-power pan law -- matches this project's level of audio fidelity elsewhere
        /// (no PLC, RMS-only level metering).</summary>
        public void SetPeerPan(string peerId, float pan)
        {
            lock (_peerTracks)
            {
                if (_peerTracks.TryGetValue(peerId, out var entry))
                {
                    PanToLeftRight(pan, out float left, out float right);
                    entry.Stereo.LeftVolume = left;
                    entry.Stereo.RightVolume = right;
                }
            }
        }

        // Never fully mutes either channel -- full hard pan sounded wrong/unnatural, so the
        // "far" channel only ever attenuates down to this floor, not silence.
        private const float MinChannelVolume = 0.45f;

        private static void PanToLeftRight(float pan, out float left, out float right)
        {
            float clamped = MathHelper.Clamp(pan, -1f, 1f);
            float attenuated = MathHelper.Lerp(1f, MinChannelVolume, Math.Abs(clamped));

            // Swapped relative to the naive reading of GainCalculator.ComputePan's sign --
            // confirmed backwards by ear via the Debug tab's loopback test.
            left = clamped >= 0f ? 1f : attenuated;
            right = clamped <= 0f ? 1f : attenuated;
        }

        public bool IsTestFilePlaying => _testFileReader != null;

        /// <summary>Debug-tab loopback test: routes a local audio file through the same
        /// gain/pan/mixer path real peer audio uses, entirely locally -- no Opus round-trip,
        /// no network -- so the falloff/pan can be checked by ear without a second player.
        /// Throws on a bad path or unsupported format; the Debug tab catches that and shows
        /// it rather than this swallowing it silently.</summary>
        public void PlayTestFile(string filePath)
        {
            StopTestFile();

            var reader = new AudioFileReader(filePath);
            ISampleProvider source = reader;

            if (reader.WaveFormat.Channels == 2)
            {
                source = new StereoToMonoSampleProvider(source) { LeftVolume = 0.5f, RightVolume = 0.5f };
            }
            else if (reader.WaveFormat.Channels != 1)
            {
                reader.Dispose();
                throw new NotSupportedException($"Unsupported channel count: {reader.WaveFormat.Channels}");
            }

            if (reader.WaveFormat.SampleRate != SampleRate)
            {
                source = new WdlResamplingSampleProvider(source, SampleRate);
            }

            var stereo = new MonoToStereoSampleProvider(new LoopingSampleProvider(reader, source));

            _testFileReader = reader;
            _testFileStereo = stereo;
            ApplyTestFileVolumes();

            _mixer.AddMixerInput(stereo);
        }

        public void StopTestFile()
        {
            if (_testFileStereo != null) _mixer.RemoveMixerInput(_testFileStereo);
            _testFileReader?.Dispose();
            _testFileReader = null;
            _testFileStereo = null;
        }

        public void SetTestFileGain(float gain)
        {
            _testFileGain = gain;
            ApplyTestFileVolumes();
        }

        public void SetTestFilePan(float pan)
        {
            _testFilePan = pan;
            ApplyTestFileVolumes();
        }

        private void ApplyTestFileVolumes()
        {
            if (_testFileStereo == null) return;

            PanToLeftRight(_testFilePan, out float left, out float right);
            _testFileStereo.LeftVolume = _testFileGain * left;
            _testFileStereo.RightVolume = _testFileGain * right;
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
                    _mixer.RemoveMixerInput(_peerTracks[id].Stereo);
                    _peerTracks.Remove(id);
                }
            }
        }

        public void Dispose()
        {
            StopCaptureAndPlayback();
            StopTestFile();
            _noiseSuppressor?.Dispose();
        }
    }
}
