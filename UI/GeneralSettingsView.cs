using System;
using System.Collections.Generic;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Blish_HUD.Settings;
using Microsoft.Xna.Framework;

namespace GW2ProximityChat
{
    /// <summary>
    /// The "General" tab: relay server address, microphone/speaker device + volume/noise-gate/
    /// activation-mode controls with a live level meter, and rebindable keybindings -- all real
    /// controls built by hand, since Blish HUD's native settings menu can't do a dynamic device
    /// dropdown and always puts a slider on every int. Laid out as bordered/titled categories
    /// sized to the full content width, same as TurtleMyWaypointWindow's map-region panels
    /// (explicit pixel widths throughout, not auto-size -- that's what TurtleMyWaypoint does).
    /// </summary>
    public class GeneralSettingsView : View
    {
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(100);

        // Mirrors TurtleMyWaypointWindow.CoreTyriaView's PanelInnerMargin/PanelSpacing constants.
        private const int PanelMargin    = 28; // content width -> panel width, leaves room for the scrollbar
        private const int CategoryPadding = 12; // OuterControlPadding on each side of a category
        private const int RowGap         = 10;  // gap between a row's label and its control
        private const int RowLabelWidth  = 190;
        private const int ExtraHeight    = 60;  // the AutoSize/content-region height math was short by this

        private readonly ProximityChatSettings _settings;
        private readonly ProximityService _proximityService;
        private readonly Action _applyServerAddress;
        private readonly Action<GeneralSettingsView> _registerActive;
        private readonly Action<GeneralSettingsView> _unregisterActive;
        private readonly int _contentWidth;
        private readonly int _contentHeight;

        private readonly List<(int Index, string Label)> _inputDevices;
        private readonly List<(int Index, string Label)> _outputDevices;

        private int _panelWidth;
        private int _innerWidth;
        private int _rowControlWidth;

        private Label _connectionLabel;
        private Label _serverNameLabel;
        private TextBox _hostTextBox;
        private TextBox _portTextBox;
        private TextBox _passwordTextBox;
        private Checkbox _micEnabledCheckbox;
        private Checkbox _noiseSuppressionCheckbox;
        private Dropdown _activationModeDropdown;
        private MicLevelMeter _micLevelMeter;
        private Dropdown _micDeviceDropdown;
        private Dropdown _speakerDeviceDropdown;

        private TimeSpan _sinceLastRefresh = TimeSpan.Zero;

        public GeneralSettingsView(
            ProximityChatSettings settings,
            ProximityService proximityService,
            Action applyServerAddress,
            int contentWidth,
            int contentHeight,
            Action<GeneralSettingsView> registerActive,
            Action<GeneralSettingsView> unregisterActive)
        {
            _settings = settings;
            _proximityService = proximityService;
            _applyServerAddress = applyServerAddress;
            _contentWidth = contentWidth;
            _contentHeight = contentHeight;
            _registerActive = registerActive;
            _unregisterActive = unregisterActive;

            _inputDevices = AudioDevices.GetInputDevices();
            _outputDevices = AudioDevices.GetOutputDevices();
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

            var serverCategory = CreateCategory(scrollPanel, "Relay Server");

            _connectionLabel = CreateLabel(serverCategory, _innerWidth);
            _serverNameLabel = CreateLabel(serverCategory, _innerWidth);

            var hostRow = CreateHRow(serverCategory);
            CreateRowLabel(hostRow, "Host:");
            _hostTextBox = new TextBox { Text = _settings.ServerHost.Value, Width = _rowControlWidth, Parent = hostRow };

            var portRow = CreateHRow(serverCategory);
            CreateRowLabel(portRow, "Port:");
            _portTextBox = new TextBox { Text = _settings.ServerPort.Value, Width = _rowControlWidth, Parent = portRow };

            var passwordRow = CreateHRow(serverCategory);
            CreateRowLabel(passwordRow, "Password:");
            _passwordTextBox = new TextBox { Text = _settings.ServerPassword.Value, Width = _rowControlWidth, Parent = passwordRow };

            var passwordNoteLabel = CreateLabel(serverCategory, _innerWidth);
            passwordNoteLabel.Text = "Blish HUD has no password-masked text field -- this is shown in plain text.";
            passwordNoteLabel.TextColor = Color.LightGray;

            var applyButton = new StandardButton { Text = "Apply", Width = _innerWidth, Height = 30, Parent = serverCategory };
            applyButton.Click += OnApplyServerClicked;
            _hostTextBox.EnterPressed += OnApplyServerClicked;
            _portTextBox.EnterPressed += OnApplyServerClicked;
            _passwordTextBox.EnterPressed += OnApplyServerClicked;

            var micCategory = CreateCategory(scrollPanel, "Microphone");

            _micEnabledCheckbox = new Checkbox
            {
                Text = "Microphone Enabled",
                Checked = _proximityService.MicrophoneEnabled,
                Parent = micCategory,
            };
            _micEnabledCheckbox.CheckedChanged += (s, e) => _proximityService.MicrophoneEnabled = _micEnabledCheckbox.Checked;

            _noiseSuppressionCheckbox = new Checkbox
            {
                Text = "Noise Suppression (rnnoise)",
                Checked = _proximityService.NoiseSuppressionEnabled,
                Parent = micCategory,
            };
            _noiseSuppressionCheckbox.CheckedChanged += (s, e) => _proximityService.NoiseSuppressionEnabled = _noiseSuppressionCheckbox.Checked;

            var modeRow = CreateHRow(micCategory);
            CreateRowLabel(modeRow, "Activation Mode:");
            _activationModeDropdown = new Dropdown { Width = _rowControlWidth, Parent = modeRow };
            _activationModeDropdown.Items.Add("Push to Talk");
            _activationModeDropdown.Items.Add("Voice Activity");
            _activationModeDropdown.SelectedItem = _settings.ActivationMode.Value == (int)MicActivationMode.VoiceActivity
                ? "Voice Activity"
                : "Push to Talk";
            _activationModeDropdown.ValueChanged += OnActivationModeChanged;

            CreateKeybindingRow(micCategory, "Push to Talk (hold)", _settings.PushToTalkKeyBinding);
            CreateKeybindingRow(micCategory, "Toggle Microphone Enabled", _settings.ToggleMicEnabledKeyBinding);

            CreateVolumeRow(micCategory, "Input Volume:", 0, 200, _settings.InputVolume.Value * 100f, percent =>
            {
                float gain = percent / 100f;
                _settings.InputVolume.Value = gain;
                _proximityService.InputVolume = gain;
            });

            CreateVolumeRow(micCategory, "Noise Gate:", 0, 30, _settings.NoiseGateThreshold.Value * 100f, percent =>
            {
                float threshold = percent / 100f;
                _settings.NoiseGateThreshold.Value = threshold;
                _proximityService.NoiseGateThreshold = threshold;
                _micLevelMeter.SetThreshold(threshold);
            });

            CreateLabel(micCategory, _innerWidth).Text = "Mic Level (live, talk to test):";
            _micLevelMeter = new MicLevelMeter(micCategory, _innerWidth, 20);
            _micLevelMeter.SetThreshold(_settings.NoiseGateThreshold.Value);
            var meterHelpLabel = CreateLabel(micCategory, _innerWidth);
            meterHelpLabel.Text = "Green/Yellow/Orange = how loud your mic currently is. Cyan line = the Noise Gate threshold above -- in Voice Activity mode, only audio louder than that line gets sent.";
            meterHelpLabel.TextColor = Color.LightGray;
            meterHelpLabel.WrapText = true;

            // Stacked (label above, full-width dropdown below) instead of side-by-side --
            // device names are long enough to get cut off next to a label at row width.
            CreateLabel(micCategory, _innerWidth).Text = "Input Device:";
            _micDeviceDropdown = CreateDeviceDropdown(micCategory, _inputDevices, _settings.MicrophoneDeviceIndex.Value, _innerWidth);
            _micDeviceDropdown.ValueChanged += OnMicDeviceChanged;

            var speakerCategory = CreateCategory(scrollPanel, "Speaker");

            CreateVolumeRow(speakerCategory, "Output Volume:", 0, 200, _settings.OutputVolume.Value * 100f, percent =>
            {
                float gain = percent / 100f;
                _settings.OutputVolume.Value = gain;
                _proximityService.OutputVolume = gain;
            });

            CreateLabel(speakerCategory, _innerWidth).Text = "Output Device:";
            _speakerDeviceDropdown = CreateDeviceDropdown(speakerCategory, _outputDevices, _settings.SpeakerDeviceIndex.Value, _innerWidth);
            _speakerDeviceDropdown.ValueChanged += OnSpeakerDeviceChanged;

            var windowCategory = CreateCategory(scrollPanel, "Window");
            CreateKeybindingRow(windowCategory, "Toggle This Window", _settings.ToggleWindowKeyBinding);

            _registerActive(this);
        }

        protected override void Unload()
        {
            _unregisterActive(this);
        }

        public void Tick(GameTime gameTime)
        {
            _sinceLastRefresh += gameTime.ElapsedGameTime;
            if (_sinceLastRefresh < RefreshInterval) return;
            _sinceLastRefresh = TimeSpan.Zero;

            _connectionLabel.Text = $"Status: {_proximityService.ConnectionStatus}";
            _serverNameLabel.Text = $"Name: {_proximityService.ServerName}";
            _micEnabledCheckbox.Checked = _proximityService.MicrophoneEnabled;
            _micLevelMeter.SetLevel(_proximityService.CurrentInputLevel);
        }

        private void OnApplyServerClicked(object sender, EventArgs e)
        {
            _settings.ServerHost.Value = _hostTextBox.Text.Trim();
            _settings.ServerPort.Value = _portTextBox.Text.Trim();
            _settings.ServerPassword.Value = _passwordTextBox.Text.Trim();
            _applyServerAddress();
        }

        private void OnActivationModeChanged(object sender, EventArgs e)
        {
            var mode = _activationModeDropdown.SelectedItem == "Voice Activity"
                ? MicActivationMode.VoiceActivity
                : MicActivationMode.PushToTalk;

            _settings.ActivationMode.Value = (int)mode;
            _proximityService.ActivationMode = mode;
        }

        private void OnMicDeviceChanged(object sender, EventArgs e)
        {
            int position = _micDeviceDropdown.Items.IndexOf(_micDeviceDropdown.SelectedItem);
            if (position < 0) return;

            int deviceIndex = _inputDevices[position].Index;
            _settings.MicrophoneDeviceIndex.Value = deviceIndex;
            _proximityService.ChangeAudioDevices(deviceIndex, _settings.SpeakerDeviceIndex.Value);
        }

        private void OnSpeakerDeviceChanged(object sender, EventArgs e)
        {
            int position = _speakerDeviceDropdown.Items.IndexOf(_speakerDeviceDropdown.SelectedItem);
            if (position < 0) return;

            int deviceIndex = _outputDevices[position].Index;
            _settings.SpeakerDeviceIndex.Value = deviceIndex;
            _proximityService.ChangeAudioDevices(_settings.MicrophoneDeviceIndex.Value, deviceIndex);
        }

        private static Dropdown CreateDeviceDropdown(FlowPanel parent, List<(int Index, string Label)> devices, int selectedIndex, int width)
        {
            var dropdown = new Dropdown { Width = width, Parent = parent };

            foreach (var device in devices)
            {
                dropdown.Items.Add(device.Label);
            }

            int position = devices.FindIndex(d => d.Index == selectedIndex);
            dropdown.SelectedItem = devices[position >= 0 ? position : 0].Label;

            return dropdown;
        }

        private void CreateKeybindingRow(FlowPanel parent, string name, SettingEntry<Blish_HUD.Input.KeyBinding> entry)
        {
            var assigner = new KeybindingAssigner(entry.Value)
            {
                KeyBindingName = name,
                NameWidth = RowLabelWidth + 30,
                Width = _innerWidth,
                Parent = parent,
            };
            assigner.BindingChanged += (s, e) => entry.Value = assigner.KeyBinding;
        }

        private void CreateVolumeRow(FlowPanel parent, string labelText, float minValue, float maxValue, float initialValue, Action<float> onChanged)
        {
            var row = CreateHRow(parent);
            CreateRowLabel(row, labelText);

            var trackBar = new TrackBar
            {
                MinValue = minValue,
                MaxValue = maxValue,
                Value = initialValue,
                SmallStep = true,
                Width = _rowControlWidth - 60,
                Parent = row,
            };

            var valueLabel = new Label
            {
                Text = $"{initialValue:0}%",
                TextColor = Color.White,
                Font = GameService.Content.DefaultFont16,
                ShowShadow = true,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Parent = row,
            };

            trackBar.ValueChanged += (s, e) =>
            {
                valueLabel.Text = $"{trackBar.Value:0}%";
                onChanged(trackBar.Value);
            };
        }

        /// <summary>A bordered, titled group spanning the full available content width -- same
        /// look/sizing approach as TurtleMyWaypointWindow's per-region panels -- used here to
        /// visually separate Relay Server / Microphone / Speaker / Window.</summary>
        private FlowPanel CreateCategory(FlowPanel parent, string title)
        {
            return new FlowPanel
            {
                Title = title,
                ShowBorder = true,
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                Width = _panelWidth,
                HeightSizingMode = SizingMode.AutoSize,
                ControlPadding = new Vector2(0, 8),
                OuterControlPadding = new Vector2(CategoryPadding, CategoryPadding),
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
    }
}
