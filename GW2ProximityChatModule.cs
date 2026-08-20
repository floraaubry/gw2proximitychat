using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Blish_HUD.Modules;
using Blish_HUD.Settings;
using Microsoft.Xna.Framework;

namespace GW2ProximityChat
{
    [Export(typeof(Module))]
    public class GW2ProximityChatModule : Module
    {
        private ModuleParameters _moduleParameters;

        private ProximityChatSettings _settings;
        private ProximityService _proximityService;
        private ProximityChatWindow _window;
        private CornerIcon _cornerIcon;
        private ContextMenuStrip _cornerMenu;

        private AsyncTexture2D _iconDisconnected;
        private AsyncTexture2D _iconConnecting;
        private AsyncTexture2D _iconConnected;

        [ImportingConstructor]
        public GW2ProximityChatModule([Import("ModuleParameters")] ModuleParameters moduleParameters)
            : base(moduleParameters)
        {
            _moduleParameters = moduleParameters;
        }

        protected override void DefineSettings(SettingCollection settings)
        {
            _settings = ProximityChatSettings.Define(settings);

            _settings.ServerHost.SettingChanged += (s, e) => ApplyServerAddress();
            _settings.ServerPort.SettingChanged += (s, e) => ApplyServerAddress();
            _settings.ServerPassword.SettingChanged += (s, e) => ApplyServerAddress();
        }

        protected override void Initialize() { }

        protected override async Task LoadAsync() { }

        protected override void OnModuleLoaded(EventArgs e)
        {
            _proximityService = new ProximityService
            {
                ServerHost = _settings.ServerHost.Value,
                ServerPort = ParsePort(_settings.ServerPort.Value),
                ServerPassword = _settings.ServerPassword.Value,
                MicrophoneEnabled = false,
                ActivationMode = (MicActivationMode)_settings.ActivationMode.Value,
                InputVolume = _settings.InputVolume.Value,
                OutputVolume = _settings.OutputVolume.Value,
                NoiseGateThreshold = _settings.NoiseGateThreshold.Value,
            };
            _proximityService.StartAudio(_settings.MicrophoneDeviceIndex.Value, _settings.SpeakerDeviceIndex.Value);

            _window = new ProximityChatWindow(_moduleParameters.ContentsManager, _settings, _proximityService, ApplyServerAddress);

            _cornerMenu = new ContextMenuStrip(BuildCornerMenuItems);

            _iconDisconnected = _moduleParameters.ContentsManager.GetTexture("disconnected.png");
            _iconConnecting = _moduleParameters.ContentsManager.GetTexture("connecting.png");
            _iconConnected = _moduleParameters.ContentsManager.GetTexture("connected.png");

            _cornerIcon = new CornerIcon
            {
                Icon = _iconDisconnected,
                BasicTooltipText = "GW2 Proximity Chat",
                Priority = 845202,
                Parent = GameService.Graphics.SpriteScreen,
            };
            _cornerIcon.Click += (s, ev) => _cornerMenu.Show(_cornerIcon);

            _settings.ToggleWindowKeyBinding.Value.Activated += OnToggleWindowActivated;
            _settings.ToggleMicEnabledKeyBinding.Value.Activated += OnToggleMicEnabledActivated;

            base.OnModuleLoaded(e);
        }

        private void ApplyServerAddress()
        {
            if (_proximityService == null) return;

            _proximityService.ServerHost = _settings.ServerHost.Value;
            _proximityService.ServerPort = ParsePort(_settings.ServerPort.Value);
            _proximityService.ServerPassword = _settings.ServerPassword.Value;
            _proximityService.ReconnectNow();
        }

        private static int ParsePort(string value)
        {
            return int.TryParse(value, out int port) && port > 0 && port <= 65535 ? port : 5847;
        }

        /// <summary>Rebuilt fresh every time the menu opens (the ContextMenuStrip factory-func
        /// constructor calls this itself) so labels always reflect current state rather than
        /// going stale while the menu is closed.</summary>
        private IEnumerable<ContextMenuStripItem> BuildCornerMenuItems()
        {
            var items = new List<ContextMenuStripItem>
            {
                new ContextMenuStripItem($"Server: {_proximityService.ServerName}") { Enabled = false },
                new ContextMenuStripItem($"Status: {_proximityService.ConnectionStatus}") { Enabled = false },
            };

            var connectItem = new ContextMenuStripItem(_proximityService.IsConnected ? "Disconnect" : "Connect");
            connectItem.Click += (s, e) =>
            {
                if (_proximityService.IsConnected) _proximityService.Disconnect();
                else _proximityService.ConnectNow();
            };
            items.Add(connectItem);

            var micItem = new ContextMenuStripItem(_proximityService.MicrophoneEnabled ? "Mute Microphone" : "Unmute Microphone");
            micItem.Click += (s, e) => _proximityService.MicrophoneEnabled = !_proximityService.MicrophoneEnabled;
            items.Add(micItem);

            var outputItem = new ContextMenuStripItem(_proximityService.OutputMuted ? "Unmute Output" : "Mute Output");
            outputItem.Click += (s, e) => _proximityService.OutputMuted = !_proximityService.OutputMuted;
            items.Add(outputItem);

            var settingsItem = new ContextMenuStripItem("Open Settings Window");
            settingsItem.Click += (s, e) => _window.ToggleWindow();
            items.Add(settingsItem);

            return items;
        }

        /// <summary>Every real setting is hidden from Blish HUD's own settings menu (see
        /// ProximityChatSettings), so without this override the module would show up there
        /// with nothing in it at all. This replaces that empty list with one button.</summary>
        public override IView GetSettingsView()
        {
            return new ModuleSettingsEntryView(() => _window?.ToggleWindow());
        }

        private void OnToggleWindowActivated(object sender, EventArgs e)
        {
            _window.ToggleWindow();
        }

        private void OnToggleMicEnabledActivated(object sender, EventArgs e)
        {
            _proximityService.MicrophoneEnabled = !_proximityService.MicrophoneEnabled;
        }

        protected override void Update(GameTime gameTime)
        {
            if (_proximityService == null) return;

            _proximityService.PushToTalkActive = _settings.PushToTalkKeyBinding.Value.IsTriggering;
            _proximityService.Tick(gameTime);

            UpdateCornerIcon();
        }

        private void UpdateCornerIcon()
        {
            var desired = _proximityService.IsConnected
                ? _iconConnected
                : _proximityService.IsConnecting
                    ? _iconConnecting
                    : _iconDisconnected;

            if (_cornerIcon.Icon != desired) _cornerIcon.Icon = desired;
        }

        protected override void Unload()
        {
            if (_settings != null)
            {
                _settings.ToggleWindowKeyBinding.Value.Activated -= OnToggleWindowActivated;
                _settings.ToggleWindowKeyBinding.Value.Enabled = false;

                _settings.ToggleMicEnabledKeyBinding.Value.Activated -= OnToggleMicEnabledActivated;
                _settings.ToggleMicEnabledKeyBinding.Value.Enabled = false;

                _settings.PushToTalkKeyBinding.Value.Enabled = false;
            }

            _cornerIcon?.Dispose();
            _cornerMenu?.Dispose();
            _window?.Dispose();
            _proximityService?.Dispose();
        }
    }
}
