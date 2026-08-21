using System;
using System.Collections.Generic;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Microsoft.Xna.Framework;

namespace GW2ProximityChat
{
    /// <summary>
    /// The "Debug" tab: raw MumbleLink read-out plus the peer roster with computed gain/pan --
    /// the empirical-validation tool for confirming ServerAddress/ShardId stability and that
    /// gain falls off correctly, kept separate from the General settings tab.
    /// </summary>
    public class DebugInfoView : View
    {
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(100);

        // Mirrors TurtleMyWaypointWindow.CoreTyriaView's PanelInnerMargin/PanelSpacing constants.
        private const int PanelMargin     = 28;
        private const int CategoryPadding = 12;
        private const int ExtraHeight     = 60; // the AutoSize/content-region height math was short by this
        private const int RowGap         = 10;
        private const int RowLabelWidth  = 190;

        // Past GainCalculator.MaxRange so the slider can show the silent tail, not just the
        // audible range.
        private const float TestDistanceSliderMax = 80f;

        private readonly ProximityService _proximityService;
        private readonly int _contentWidth;
        private readonly int _contentHeight;
        private readonly Action<DebugInfoView> _registerActive;
        private readonly Action<DebugInfoView> _unregisterActive;

        private int _panelWidth;
        private int _innerWidth;
        private int _rowControlWidth;

        private FlowPanel _peersCategory;

        private TextBox _testFilePathTextBox;
        private StandardButton _testFilePlayButton;
        private Label _testFileStatusLabel;
        private TrackBar _testDistanceTrackBar;
        private Label _testDistanceValueLabel;
        private TrackBar _testPanTrackBar;
        private Label _testPanValueLabel;

        private Label _availableLabel;
        private Label _characterLabel;
        private Label _mapLabel;
        private Label _positionLabel;
        private Label _forwardLabel;
        private Label _serverAddressLabel;
        private Label _shardLabel;
        private Label _rawInstanceLabel;
        private Label _instanceKeyLabel;
        private Label _tickLabel;
        private Checkbox _rangeIndicatorCheckbox;
        private Checkbox _fakeUsersCheckbox;

        private Label _peersHeaderLabel;
        private readonly Dictionary<string, Label> _peerLabels = new Dictionary<string, Label>();

        private TimeSpan _sinceLastRefresh = TimeSpan.Zero;

        public DebugInfoView(
            ProximityService proximityService,
            int contentWidth,
            int contentHeight,
            Action<DebugInfoView> registerActive,
            Action<DebugInfoView> unregisterActive)
        {
            _proximityService = proximityService;
            _contentWidth = contentWidth;
            _contentHeight = contentHeight;
            _registerActive = registerActive;
            _unregisterActive = unregisterActive;
        }

        protected override void Build(Container buildPanel)
        {
            _panelWidth = _contentWidth - PanelMargin;
            _innerWidth = _panelWidth - CategoryPadding * 2;
            _rowControlWidth = _innerWidth - RowLabelWidth - RowGap;

            var scrollPanel = new FlowPanel
            {
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                ControlPadding = new Vector2(0, 14),
                Size = new Point(_contentWidth, _contentHeight + ExtraHeight),
                CanScroll = true,
                Parent = buildPanel,
            };

            var mumbleCategory = CreateCategory(scrollPanel, "MumbleLink");
            _availableLabel     = CreateRow(mumbleCategory);
            _characterLabel     = CreateRow(mumbleCategory);
            _mapLabel           = CreateRow(mumbleCategory);
            _positionLabel      = CreateRow(mumbleCategory);
            _forwardLabel       = CreateRow(mumbleCategory);
            _serverAddressLabel = CreateRow(mumbleCategory);
            _shardLabel         = CreateRow(mumbleCategory);
            _rawInstanceLabel   = CreateRow(mumbleCategory);
            _instanceKeyLabel   = CreateRow(mumbleCategory);
            _tickLabel          = CreateRow(mumbleCategory);

            _rangeIndicatorCheckbox = new Checkbox
            {
                Text = "Show Range Indicator (draws Min/MaxRange rings around your character)",
                Checked = _proximityService.ShowRangeIndicator,
                Parent = mumbleCategory,
            };
            _rangeIndicatorCheckbox.CheckedChanged += (s, e) => _proximityService.ShowRangeIndicator = _rangeIndicatorCheckbox.Checked;

            _fakeUsersCheckbox = new Checkbox
            {
                Text = "Show 20 Fake Peers (adds fake rows to the Peers list below and the Users window, for checking layout without a second player)",
                Checked = _proximityService.ShowFakeDebugPeers,
                Parent = mumbleCategory,
            };
            _fakeUsersCheckbox.CheckedChanged += (s, e) => _proximityService.ShowFakeDebugPeers = _fakeUsersCheckbox.Checked;

            BuildAudioTestCategory(scrollPanel);

            _peersCategory = CreateCategory(scrollPanel, "Peers");
            _peersHeaderLabel = CreateRow(_peersCategory);
            _peersHeaderLabel.Text = "Peers:";

            _registerActive(this);
        }

        protected override void Unload()
        {
            _proximityService.StopTestFile();
            _unregisterActive(this);
        }

        /// <summary>Loopback test: plays a local file through the same gain/pan/mixer path
        /// real peer audio uses (see AudioService.PlayTestFile) so the falloff/pan curves
        /// can be checked by ear without a second player. Distance slider drives gain through
        /// the real GainCalculator.DistanceToGain curve -- not a raw gain slider -- so it's
        /// actually exercising the tiered falloff, not just proving volume knobs work.</summary>
        private void BuildAudioTestCategory(FlowPanel parent)
        {
            var category = CreateCategory(parent, "Audio Test (Loopback, no second player needed)");

            var pathLabel = CreateLabel(category, _innerWidth);
            pathLabel.Text = "File Path (wav/mp3/wma):";

            _testFilePathTextBox = new TextBox { Width = _innerWidth, Parent = category };

            _testFilePlayButton = new StandardButton { Text = "Play", Width = _innerWidth, Height = 30, Parent = category };
            _testFilePlayButton.Click += OnTestFilePlayClicked;
            _testFilePathTextBox.EnterPressed += OnTestFilePlayClicked;

            _testFileStatusLabel = CreateLabel(category, _innerWidth);
            _testFileStatusLabel.Text = "Stopped";
            _testFileStatusLabel.TextColor = Color.LightGray;

            var distanceRow = CreateHRow(category);
            CreateRowLabel(distanceRow, "Simulated Distance:");
            _testDistanceTrackBar = new TrackBar
            {
                MinValue = 0f,
                MaxValue = TestDistanceSliderMax,
                Value = 20f,
                SmallStep = true,
                Width = _rowControlWidth - 90,
                Parent = distanceRow,
            };
            _testDistanceValueLabel = CreateLabel(distanceRow, 80);
            _testDistanceTrackBar.ValueChanged += OnTestDistanceChanged;
            OnTestDistanceChanged(_testDistanceTrackBar, EventArgs.Empty);

            var panRow = CreateHRow(category);
            CreateRowLabel(panRow, "Simulated Pan:");
            _testPanTrackBar = new TrackBar
            {
                MinValue = -1f,
                MaxValue = 1f,
                Value = 0f,
                SmallStep = true,
                Width = _rowControlWidth - 90,
                Parent = panRow,
            };
            _testPanValueLabel = CreateLabel(panRow, 80);
            _testPanTrackBar.ValueChanged += OnTestPanChanged;
            OnTestPanChanged(_testPanTrackBar, EventArgs.Empty);
        }

        private void OnTestFilePlayClicked(object sender, EventArgs e)
        {
            if (_proximityService.IsTestFilePlaying)
            {
                _proximityService.StopTestFile();
                _testFilePlayButton.Text = "Play";
                _testFileStatusLabel.Text = "Stopped";
                _testFileStatusLabel.TextColor = Color.LightGray;
                return;
            }

            try
            {
                _proximityService.PlayTestFile(_testFilePathTextBox.Text);
                _proximityService.SetTestFileGain(GainCalculator.DistanceToGain(_testDistanceTrackBar.Value));
                _proximityService.SetTestFilePan(_testPanTrackBar.Value);

                _testFilePlayButton.Text = "Stop";
                _testFileStatusLabel.Text = $"Playing: {System.IO.Path.GetFileName(_testFilePathTextBox.Text)} (looping)";
                _testFileStatusLabel.TextColor = Color.White;
            }
            catch (Exception ex)
            {
                _testFileStatusLabel.Text = $"Failed to play: {ex.Message}";
                _testFileStatusLabel.TextColor = new Color(255, 120, 120);
            }
        }

        private void OnTestDistanceChanged(object sender, EventArgs e)
        {
            float distance = _testDistanceTrackBar.Value;
            float gain = GainCalculator.DistanceToGain(distance);
            _testDistanceValueLabel.Text = $"{distance:0}m ({gain * 100:0}%)";
            _proximityService.SetTestFileGain(gain);
        }

        private void OnTestPanChanged(object sender, EventArgs e)
        {
            float pan = _testPanTrackBar.Value;
            _testPanValueLabel.Text = $"{pan:0.00}";
            _proximityService.SetTestFilePan(pan);
        }

        private FlowPanel CreateCategory(FlowPanel parent, string title)
        {
            return new FlowPanel
            {
                Title = title,
                ShowBorder = true,
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                Width = _panelWidth,
                HeightSizingMode = SizingMode.AutoSize,
                ControlPadding = new Vector2(0, 6),
                OuterControlPadding = new Vector2(CategoryPadding, CategoryPadding),
                Parent = parent,
            };
        }

        private Label CreateRow(FlowPanel parent)
        {
            return new Label
            {
                Text = "-",
                TextColor = Color.White,
                Font = GameService.Content.DefaultFont16,
                ShowShadow = true,
                AutoSizeHeight = true,
                Width = _innerWidth,
                Parent = parent,
            };
        }

        private FlowPanel CreateHRow(FlowPanel parent)
        {
            return new FlowPanel
            {
                FlowDirection = ControlFlowDirection.LeftToRight,
                Width = _innerWidth,
                HeightSizingMode = SizingMode.AutoSize,
                ControlPadding = new Vector2(RowGap, 0),
                Parent = parent,
            };
        }

        private static Label CreateRowLabel(FlowPanel row, string text)
        {
            return new Label
            {
                Text = text,
                TextColor = Color.White,
                Font = GameService.Content.DefaultFont16,
                ShowShadow = true,
                AutoSizeHeight = true,
                Width = RowLabelWidth,
                Parent = row,
            };
        }

        private static Label CreateLabel(FlowPanel parent, int width)
        {
            return new Label
            {
                Text = "-",
                TextColor = Color.White,
                Font = GameService.Content.DefaultFont16,
                ShowShadow = true,
                AutoSizeHeight = true,
                Width = width,
                Parent = parent,
            };
        }

        public void Tick(GameTime gameTime)
        {
            _sinceLastRefresh += gameTime.ElapsedGameTime;
            if (_sinceLastRefresh < RefreshInterval) return;
            _sinceLastRefresh = TimeSpan.Zero;

            RefreshMumbleSection();
            RefreshPeersSection();
        }

        private void RefreshMumbleSection()
        {
            var mumble = GameService.Gw2Mumble;
            bool available = mumble.IsAvailable;

            _availableLabel.Text = $"Mumble Available: {available}";

            if (!available)
            {
                _characterLabel.Text     = "Character: -";
                _mapLabel.Text           = "Map Id: -";
                _positionLabel.Text      = "Position: -";
                _forwardLabel.Text       = "Forward: -";
                _serverAddressLabel.Text = "Server Address: -";
                _shardLabel.Text         = "Shard Id: -";
                _rawInstanceLabel.Text   = "Raw Instance: -";
                _instanceKeyLabel.Text   = "Instance Key: -";
                _tickLabel.Text          = "Tick / Staleness: -";
                return;
            }

            bool stale = mumble.TimeSinceTick > TimeSpan.FromSeconds(1);

            var position = mumble.PlayerCharacter.Position;
            var forward = mumble.PlayerCharacter.Forward;
            string serverAddress = mumble.Info.ServerAddress;
            uint shardId = mumble.Info.ShardId;
            uint rawInstance = mumble.RawClient.Instance;
            string instanceKey = StableHash.InstanceKey(serverAddress, shardId);

            _characterLabel.Text     = $"Character: {mumble.PlayerCharacter.Name}";
            _mapLabel.Text           = $"Map Id: {mumble.CurrentMap.Id}";
            _positionLabel.Text      = $"Position: {position.X:0.0}, {position.Y:0.0}, {position.Z:0.0}";
            _forwardLabel.Text       = $"Forward: {forward.X:0.00}, {forward.Y:0.00}, {forward.Z:0.00}";
            _serverAddressLabel.Text = $"Server Address: {serverAddress}:{mumble.Info.ServerPort}";
            _shardLabel.Text         = $"Shard Id: {shardId}";
            _rawInstanceLabel.Text   = $"Raw Instance: {rawInstance}";
            _instanceKeyLabel.Text   = $"Instance Key: {instanceKey}";
            _tickLabel.Text          = stale
                ? $"Tick / Staleness: {mumble.Tick} (STALE, {mumble.TimeSinceTick.TotalSeconds:0.0}s)"
                : $"Tick / Staleness: {mumble.Tick} (live)";
        }

        private void RefreshPeersSection()
        {
            var peers = _proximityService.GetPeersSnapshot();
            var seen = new HashSet<string>();

            if (peers.Count == 0)
            {
                _peersHeaderLabel.Text = "Peers: (none in range)";
            }
            else
            {
                _peersHeaderLabel.Text = $"Peers: ({peers.Count})";

                foreach (var peer in peers)
                {
                    seen.Add(peer.PlayerId);

                    if (!_peerLabels.TryGetValue(peer.PlayerId, out var label))
                    {
                        label = CreateRow(_peersCategory);
                        _peerLabels[peer.PlayerId] = label;
                    }

                    label.Text = $"  {peer.Name}: {peer.Distance:0} units, gain {peer.Gain:0.00}, pan {peer.Pan:0.00}";
                }
            }

            List<string> stalePeerIds = null;
            foreach (var id in _peerLabels.Keys)
            {
                if (!seen.Contains(id))
                {
                    (stalePeerIds ?? (stalePeerIds = new List<string>())).Add(id);
                }
            }

            if (stalePeerIds == null) return;

            foreach (var id in stalePeerIds)
            {
                _peerLabels[id].Dispose();
                _peerLabels.Remove(id);
            }
        }
    }
}
