using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Blish_HUD;
using Microsoft.Xna.Framework;

namespace GW2ProximityChat
{
    public class LocalPeerState
    {
        public string PlayerId;
        public string Name;
        public Vector3 Position;
        public Vector3 Facing;
        public float Distance;
        public float Gain;
        public float Pan;
    }

    /// <summary>
    /// Coordinates MumbleLink read-out, the relay connection, and the audio pipeline: reads the
    /// local player's position, throttles state sends to the server, turns the returned peer
    /// roster into per-peer gain/pan, and feeds that gain into the audio mixer.
    /// </summary>
    public class ProximityService : IDisposable
    {
        private static readonly Logger Logger = Logger.GetLogger<ProximityService>();

        private static readonly TimeSpan StateSendInterval = TimeSpan.FromMilliseconds(100); // 10Hz

        // Only used while MumbleLink is unavailable (GW2 not running/loading), so the connection
        // still sees periodic traffic and doesn't sit idle -- state sends already cover this at
        // 10Hz whenever MumbleLink is available.
        private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(15);

        private readonly RelayClient _relayClient;
        private readonly AudioService _audioService;
        private readonly string _playerId = Guid.NewGuid().ToString("N");

        private readonly List<LocalPeerState> _peers = new List<LocalPeerState>();
        private readonly object _peersLock = new object();

        private TimeSpan _sinceLastStateSend = TimeSpan.Zero;
        private bool _connecting;

        // Set when the server rejects our password -- surfaced in the corner menu/UI rather
        // than acted on automatically, since there's no auto-reconnect to suppress any more.
        private bool _authFailed;

        public string ServerHost { get; set; } = "127.0.0.1";
        public int ServerPort { get; set; } = 5847;
        public string ServerPassword { get; set; } = "";

        public string ServerName { get; private set; } = "-";

        public bool MicrophoneEnabled
        {
            get => _audioService.MicrophoneEnabled;
            set => _audioService.MicrophoneEnabled = value;
        }

        public bool NoiseSuppressionEnabled
        {
            get => _audioService.NoiseSuppressionEnabled;
            set => _audioService.NoiseSuppressionEnabled = value;
        }

        public MicActivationMode ActivationMode
        {
            get => _audioService.ActivationMode;
            set => _audioService.ActivationMode = value;
        }

        /// <summary>Set every tick from the Push-to-Talk keybinding's held state.</summary>
        public bool PushToTalkActive
        {
            get => _audioService.PushToTalkActive;
            set => _audioService.PushToTalkActive = value;
        }

        public float CurrentInputLevel => _audioService.CurrentInputLevel;

        public float NoiseGateThreshold
        {
            get => _audioService.NoiseGateThreshold;
            set => _audioService.NoiseGateThreshold = value;
        }

        public float InputVolume
        {
            get => _audioService.InputVolume;
            set => _audioService.InputVolume = value;
        }

        public float OutputVolume
        {
            get => _audioService.OutputVolume;
            set => _audioService.OutputVolume = value;
        }

        public bool OutputMuted
        {
            get => _audioService.OutputMuted;
            set => _audioService.OutputMuted = value;
        }

        public bool IsConnected => _relayClient.IsConnected;
        public bool IsConnecting => _connecting;
        public string ConnectionStatus { get; private set; } = "Disconnected";

        /// <summary>Debug-tab toggle -- draws GainCalculator's Min/MaxRange as ground-plane
        /// rings around the player (see <see cref="RangeIndicatorEntity"/>) for visually
        /// calibrating the proximity falloff in-game.</summary>
        public bool ShowRangeIndicator { get; set; }

        /// <summary>Debug-tab toggle -- appends <see cref="FakeDebugPeers"/> to every
        /// <see cref="GetPeersSnapshot"/> call so the Users window/Debug peers list can be
        /// checked visually (layout, scrolling, row controls) without a second real player.
        /// Kept out of the real <see cref="_peers"/> list so it never touches the relay/audio
        /// pipeline -- it's purely additive at the UI-facing snapshot.</summary>
        public bool ShowFakeDebugPeers { get; set; }

        private const int FakeDebugPeerCount = 20;

        private static readonly LocalPeerState[] FakeDebugPeers = BuildFakeDebugPeers();

        private static LocalPeerState[] BuildFakeDebugPeers()
        {
            var peers = new LocalPeerState[FakeDebugPeerCount];
            for (int i = 0; i < FakeDebugPeerCount; i++)
            {
                float t = i / (float)(FakeDebugPeerCount - 1); // 0..1 across the whole set
                peers[i] = new LocalPeerState
                {
                    PlayerId = $"debug-fake-{i + 1}",
                    Name = $"Fake Peer {i + 1}",
                    Distance = 2f + t * 38f,
                    Gain = 1f - t * 0.9f,
                    Pan = -1f + t * 2f,
                };
            }

            return peers;
        }

        /// <summary>Debug-tab loopback test -- plays a local audio file through the same
        /// gain/pan path real peers use, so falloff/pan can be checked by ear solo.</summary>
        public bool IsTestFilePlaying => _audioService.IsTestFilePlaying;
        public void PlayTestFile(string filePath) => _audioService.PlayTestFile(filePath);
        public void StopTestFile() => _audioService.StopTestFile();
        public void SetTestFileGain(float gain) => _audioService.SetTestFileGain(gain);
        public void SetTestFilePan(float pan) => _audioService.SetTestFilePan(pan);

        public ProximityService()
        {
            _relayClient = new RelayClient();
            _relayClient.PeersReceived += OnPeersReceived;
            _relayClient.AudioFrameReceived += OnAudioFrameReceived;
            _relayClient.Connected += () =>
            {
                ConnectionStatus = "Connected";
                Logger.Info("Connected to relay server {0}:{1}", ServerHost, ServerPort);
            };
            _relayClient.Disconnected += ex =>
            {
                if (_authFailed) return; // ConnectionStatus/logging already reflects the auth failure

                ConnectionStatus = ex == null ? "Disconnected" : $"Disconnected ({ex.Message})";
                if (ex == null)
                {
                    Logger.Warn("Disconnected from relay server {0}:{1} (server closed the connection)", ServerHost, ServerPort);
                }
                else
                {
                    Logger.Warn(ex, "Lost connection to relay server {0}:{1}", ServerHost, ServerPort);
                }
            };
            _relayClient.ServerHelloReceived += (name, version) =>
            {
                ServerName = name;
                Logger.Info("Relay server '{0}' version {1}", name, version);
            };
            _relayClient.AuthFailed += reason =>
            {
                _authFailed = true;
                ConnectionStatus = $"Auth failed: {reason}";
                Logger.Warn("Relay server {0}:{1} rejected our password: {2}", ServerHost, ServerPort, reason);
            };
            _relayClient.VersionMismatch += (serverVer, clientVer) =>
            {
                ConnectionStatus = $"Version mismatch (server {serverVer}, client {clientVer})";
                Logger.Warn("Incompatible server version {0} (client is {1})", serverVer, clientVer);
            };
            _relayClient.ServerFull += () =>
            {
                ConnectionStatus = "Server full";
                Logger.Warn("Relay server {0}:{1} is full", ServerHost, ServerPort);
            };

            _audioService = new AudioService();
            _audioService.EncodedFrameReady += OnEncodedFrameReady;
        }

        public void StartAudio(int inputDevice, int outputDevice)
        {
            _audioService.Start(inputDevice, outputDevice);
        }

        /// <summary>Switches capture/playback devices live (e.g. the user picked a different one).</summary>
        public void ChangeAudioDevices(int inputDevice, int outputDevice)
        {
            _audioService.Start(inputDevice, outputDevice);
        }

        /// <summary>User-initiated connect (corner menu, or the settings window's Apply button
        /// when a connection was already active/attempted). There is no automatic connect or
        /// reconnect anywhere else -- if the connection drops, it stays dropped until the user
        /// asks to connect again.</summary>
        public void ConnectNow()
        {
            if (_relayClient.IsConnected || _connecting) return;

            _authFailed = false;
            _connecting = true;
            _ = ConnectAndResetFlagAsync();
        }

        public void Disconnect()
        {
            Logger.Info("Disconnecting from relay server {0}:{1} (user requested)", ServerHost, ServerPort);
            _relayClient.Disconnect();
            ConnectionStatus = "Disconnected";
        }

        /// <summary>Called after the user changes server host/port/password in the settings
        /// window and hits Apply. Only reconnects if a connection was already active or being
        /// attempted (including a prior auth failure) -- applying new settings from a cold,
        /// never-connected state shouldn't itself count as "connect".</summary>
        public void ReconnectNow()
        {
            bool shouldReconnect = _relayClient.IsConnected || _connecting || _authFailed;

            Logger.Info("Applying new relay server settings ({0}:{1})", ServerHost, ServerPort);
            _relayClient.Disconnect();
            ConnectionStatus = "Disconnected";
            ServerName = "-";
            _authFailed = false;

            if (shouldReconnect) ConnectNow();
        }

        public List<LocalPeerState> GetPeersSnapshot()
        {
            lock (_peersLock)
            {
                var snapshot = new List<LocalPeerState>(_peers);
                if (ShowFakeDebugPeers) snapshot.AddRange(FakeDebugPeers);
                return snapshot;
            }
        }

        /// <summary>User-set per-peer volume (Users window) -- independent of the distance-based
        /// gain RecomputeGains applies every tick, the two are combined in AudioService.</summary>
        public float GetPeerVolume(string peerId) => _audioService.GetPeerVolume(peerId);
        public void SetPeerVolume(string peerId, float volume) => _audioService.SetPeerVolume(peerId, volume);
        public bool IsPeerMuted(string peerId) => _audioService.IsPeerMuted(peerId);
        public bool IsPeerTalking(string peerId) => _audioService.IsPeerTalking(peerId);
        public void SetPeerMuted(string peerId, bool muted) => _audioService.SetPeerMuted(peerId, muted);

        public void Tick(GameTime gameTime)
        {
            var mumble = GameService.Gw2Mumble;

            if (!mumble.IsAvailable)
            {
                TickKeepAlive(gameTime);
                return;
            }

            var position = mumble.PlayerCharacter.Position;
            var forward = mumble.PlayerCharacter.Forward;

            RecomputeGains(position, forward);

            _sinceLastStateSend += gameTime.ElapsedGameTime;
            if (_sinceLastStateSend < StateSendInterval) return;
            _sinceLastStateSend = TimeSpan.Zero;

            if (!_relayClient.IsConnected) return;

            string instanceKey = StableHash.InstanceKey(mumble.Info.ServerAddress, mumble.Info.ShardId);

            var state = new StateMessage
            {
                PlayerId = _playerId,
                Name = mumble.PlayerCharacter.Name,
                MapId = mumble.CurrentMap.Id,
                InstanceKey = instanceKey,
                Pos = new[] { position.X, position.Y, position.Z },
                Facing = new[] { forward.X, forward.Y, forward.Z },
                Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Password = ServerPassword,
            };

            _ = _relayClient.SendStateAsync(state);
        }

        private void TickKeepAlive(GameTime gameTime)
        {
            _sinceLastStateSend += gameTime.ElapsedGameTime;
            if (_sinceLastStateSend < KeepAliveInterval) return;
            _sinceLastStateSend = TimeSpan.Zero;

            if (!_relayClient.IsConnected) return;
            _ = _relayClient.SendKeepAliveAsync();
        }

        private async Task ConnectAndResetFlagAsync()
        {
            try
            {
                ConnectionStatus = "Connecting...";
                Logger.Info("Connecting to relay server {0}:{1}...", ServerHost, ServerPort);
                await _relayClient.ConnectAsync(ServerHost, ServerPort);
            }
            catch (Exception ex)
            {
                ConnectionStatus = $"Connect failed ({ex.Message})";
                Logger.Warn(ex, "Failed to connect to relay server {0}:{1}", ServerHost, ServerPort);
            }
            finally
            {
                _connecting = false;
            }
        }

        private void RecomputeGains(Vector3 listenerPosition, Vector3 listenerForward)
        {
            lock (_peersLock)
            {
                foreach (var peer in _peers)
                {
                    peer.Distance = Vector3.Distance(listenerPosition, peer.Position);
                    peer.Gain = GainCalculator.DistanceToGain(peer.Distance);
                    peer.Pan = GainCalculator.ComputePan(listenerPosition, listenerForward, peer.Position);
                    _audioService.SetPeerGain(peer.PlayerId, peer.Gain);
                    _audioService.SetPeerPan(peer.PlayerId, peer.Pan);
                }
            }
        }

        private void OnPeersReceived(PeerSnapshot[] snapshots)
        {
            lock (_peersLock)
            {
                _peers.Clear();
                foreach (var s in snapshots)
                {
                    if (s.PlayerId == _playerId) continue;
                    _peers.Add(new LocalPeerState
                    {
                        PlayerId = s.PlayerId,
                        Name = s.Name,
                        Position = s.Position,
                        Facing = s.Facing,
                    });
                }
            }

            var activeIds = new HashSet<string>(snapshots.Select(s => s.PlayerId));
            _audioService.RetainOnly(activeIds);
        }

        private void OnAudioFrameReceived(string peerId, byte[] opus)
        {
            _audioService.SubmitPeerAudio(peerId, opus);
        }

        private void OnEncodedFrameReady(byte[] opusFrame)
        {
            if (!_relayClient.IsConnected) return;
            _ = _relayClient.SendAudioAsync(opusFrame);
        }

        public void Dispose()
        {
            _relayClient.Dispose();
            _audioService.Dispose();
        }
    }
}
