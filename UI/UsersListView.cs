using System;
using System.Collections.Generic;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;

namespace GW2ProximityChat
{
    /// <summary>
    /// Scrollable roster of every peer currently in range: name, a per-peer volume slider, and
    /// a mute/unmute button. Refreshed on a timer (same polling pattern as DebugInfoView's peer
    /// section) rather than an event, so it never has to cross from the relay's network thread
    /// into UI code -- ProximityService.GetPeersSnapshot() is already thread-safe for this.
    /// Rows are created once per peer and updated in place so an in-progress slider drag or the
    /// mute button's state survives a refresh.
    ///
    /// Plain class rather than a Blish_HUD View -- UsersWindow has no tabs to host a View
    /// through, so this just builds its controls straight into the window like any other panel.
    /// No bordered/titled category panel wrapping the rows -- the window's own chrome already
    /// frames the content, so an inner panel was just a redundant box within a box; peer rows sit
    /// directly in the scroll container.
    /// </summary>
    public class UsersListView
    {
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(250);

        private const int ScrollbarMargin = 28; // clearance for the scroll container's scrollbar
        private const int ExtraHeight     = 60;
        private const int RowGap          = 6;

        // No percentage label any more -- its column goes to the slider instead. Name/icon/mute
        // stay fixed width; _volumeColumnWidth (below) is computed per-instance from whatever's
        // left over so the slider actually fills the row instead of leaving unused space before
        // the scrollbar margin.
        private const int NameColumnWidth = 70;
        private const int MuteButtonWidth = 60;

        private const int TalkingIconSize      = 20;
        private const float TalkingOpacity     = 1f;
        private const float NotTalkingOpacity  = 0.25f;

        // ControlFlowDirection.LeftToRight top-aligns every child in a row to the same Y rather
        // than centering them relative to each other, so mismatched control heights read as
        // misaligned even though each control is individually positioned correctly -- every row
        // control shares this one explicit height so they line up.
        private const int RowControlHeight = 26;

        // TrackBar's draggable nub is hardcoded to Y=0 inside its own Paint step (not centered/
        // proportional to Height), so stretching TrackBar's own Height to RowControlHeight leaves
        // the nub pinned near the top instead of centered -- confirmed against source. TrackBar is
        // instead kept at its native height and placed inside a plain (non-flow) Panel slot sized
        // to RowControlHeight, with the TrackBar's Location manually centered inside that slot;
        // a plain Panel doesn't auto-reposition children the way FlowPanel does, so the manual
        // offset sticks across relayouts.
        private const int TrackBarNativeHeight = 16;

        // Padding added around each row's controls (top+bottom), independent of RowControlHeight.
        private const int RowVerticalPadding = 6;

        private class PeerRow
        {
            public FlowPanel Row;
            public Image TalkingIcon;
            public Label NameLabel;
            public TrackBar VolumeTrackBar;
            public StandardButton MuteButton;
        }

        private readonly ProximityService _proximityService;
        private readonly AsyncTexture2D _talkingIconTexture;
        private readonly int _innerWidth;
        private readonly int _volumeColumnWidth;

        private readonly FlowPanel _scrollPanel;
        private readonly Label _emptyLabel;

        private readonly Dictionary<string, PeerRow> _peerRows = new Dictionary<string, PeerRow>();

        private TimeSpan _sinceLastRefresh = TimeSpan.Zero;

        public UsersListView(ProximityService proximityService, AsyncTexture2D talkingIconTexture, int contentWidth, int contentHeight, Container parent)
        {
            _proximityService = proximityService;
            _talkingIconTexture = talkingIconTexture;

            _innerWidth = contentWidth - ScrollbarMargin;

            // Icon + name + slider + mute button, 3 gaps between the 4 of them -- whatever's left
            // over after the fixed-width columns goes to the slider, minus a further 10px trim.
            _volumeColumnWidth = _innerWidth - TalkingIconSize - NameColumnWidth - MuteButtonWidth - RowGap * 3 - 10;

            _scrollPanel = new FlowPanel
            {
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                ControlPadding = new Vector2(0, 8),
                Size = new Point(contentWidth, contentHeight + ExtraHeight),
                CanScroll = true,
                Parent = parent,
            };

            _emptyLabel = new Label
            {
                Text = "(no other players in range)",
                TextColor = Color.LightGray,
                Font = GameService.Content.DefaultFont16,
                ShowShadow = true,
                AutoSizeHeight = true,
                Width = _innerWidth,
                Parent = _scrollPanel,
            };
        }

        public void Tick(GameTime gameTime)
        {
            _sinceLastRefresh += gameTime.ElapsedGameTime;
            if (_sinceLastRefresh < RefreshInterval) return;
            _sinceLastRefresh = TimeSpan.Zero;

            RefreshUsers();
        }

        private void RefreshUsers()
        {
            var peers = _proximityService.GetPeersSnapshot();

            _emptyLabel.Visible = peers.Count == 0;

            var seen = new HashSet<string>();
            foreach (var peer in peers)
            {
                seen.Add(peer.PlayerId);

                if (!_peerRows.TryGetValue(peer.PlayerId, out var row))
                {
                    row = CreatePeerRow(peer.PlayerId, peer.Name);
                    _peerRows[peer.PlayerId] = row;
                }

                row.NameLabel.Text = peer.Name;
                row.TalkingIcon.Opacity = _proximityService.IsPeerTalking(peer.PlayerId) ? TalkingOpacity : NotTalkingOpacity;
            }

            List<string> stalePeerIds = null;
            foreach (var id in _peerRows.Keys)
            {
                if (!seen.Contains(id))
                {
                    (stalePeerIds ?? (stalePeerIds = new List<string>())).Add(id);
                }
            }

            if (stalePeerIds == null) return;

            foreach (var id in stalePeerIds)
            {
                _peerRows[id].Row.Dispose();
                _peerRows.Remove(id);
            }
        }

        private PeerRow CreatePeerRow(string peerId, string name)
        {
            var row = new FlowPanel
            {
                FlowDirection = ControlFlowDirection.LeftToRight,
                Width = _innerWidth,
                HeightSizingMode = SizingMode.AutoSize,
                ControlPadding = new Vector2(RowGap, 0),
                // Vertical breathing room around the row's controls -- padding the row itself
                // rather than the controls' own Height, since stretching TrackBar's Height is what
                // broke the nub's alignment last time. Shifts every child down by the same amount,
                // so it doesn't reintroduce any relative misalignment between them.
                OuterControlPadding = new Vector2(0, RowVerticalPadding),
                Parent = _scrollPanel,
            };

            var talkingIcon = new Image
            {
                Texture = _talkingIconTexture,
                Size = new Point(TalkingIconSize, TalkingIconSize),
                Opacity = NotTalkingOpacity,
                Parent = row,
            };

            var nameLabel = new Label
            {
                Text = name,
                TextColor = Color.White,
                Font = GameService.Content.DefaultFont16,
                ShowShadow = true,
                VerticalAlignment = VerticalAlignment.Middle,
                Width = NameColumnWidth,
                Height = RowControlHeight,
                Parent = row,
            };

            var volumeTrackBarSlot = new Panel
            {
                Width = _volumeColumnWidth,
                Height = RowControlHeight,
                ShowBorder = false,
                Parent = row,
            };

            float initialVolumePercent = _proximityService.GetPeerVolume(peerId) * 100f;
            var volumeTrackBar = new TrackBar
            {
                MinValue = 0,
                MaxValue = 200,
                Value = initialVolumePercent,
                SmallStep = true,
                Width = _volumeColumnWidth,
                Location = new Point(0, (RowControlHeight - TrackBarNativeHeight) / 2),
                Parent = volumeTrackBarSlot,
            };

            volumeTrackBar.ValueChanged += (s, e) =>
            {
                _proximityService.SetPeerVolume(peerId, volumeTrackBar.Value / 100f);
            };

            bool muted = _proximityService.IsPeerMuted(peerId);
            var muteButton = new StandardButton
            {
                Text = muted ? "Unmute" : "Mute",
                Width = MuteButtonWidth,
                Height = RowControlHeight,
                Parent = row,
            };
            muteButton.Click += (s, e) =>
            {
                bool newMuted = !_proximityService.IsPeerMuted(peerId);
                _proximityService.SetPeerMuted(peerId, newMuted);
                muteButton.Text = newMuted ? "Unmute" : "Mute";
            };

            return new PeerRow
            {
                Row = row,
                TalkingIcon = talkingIcon,
                NameLabel = nameLabel,
                VolumeTrackBar = volumeTrackBar,
                MuteButton = muteButton,
            };
        }
    }
}
