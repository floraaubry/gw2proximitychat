using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Blish_HUD.Modules.Managers;
using Microsoft.Xna.Framework;

namespace GW2ProximityChat
{
    /// <summary>
    /// Tabbed window ("General" settings + "Debug" info), same region/content dimensions and
    /// background texture as this user's other Blish HUD modules (WIMPSNA, TurtleMyWaypoint).
    /// </summary>
    public class ProximityChatWindow : TabbedWindow2
    {
        private const int LeftOffset  = 58;
        private const int RightMargin = 20;

        private const int WindowRegionWidth = 913;
        private const int ContentWidth      = WindowRegionWidth - LeftOffset - RightMargin;
        private const int ContentHeight     = 636;

        private readonly ProximityChatSettings _settings;
        private readonly ProximityService _proximityService;
        private readonly System.Action _applyServerAddress;

        private readonly Tab _generalTab;
        private readonly Tab _debugTab;

        private GeneralSettingsView _activeGeneralView;
        private DebugInfoView _activeDebugView;

        public ProximityChatWindow(
            ContentsManager contentsManager,
            ProximityChatSettings settings,
            ProximityService proximityService,
            System.Action applyServerAddress)
            : base(
                contentsManager.GetTexture("window_bg.png"),
                new Rectangle(40, 26, WindowRegionWidth, 691),
                new Rectangle(40 + LeftOffset, 40, ContentWidth, ContentHeight))
        {
            _settings = settings;
            _proximityService = proximityService;
            _applyServerAddress = applyServerAddress;

            Parent = GameService.Graphics.SpriteScreen;
            Title = "GW2 Proximity Chat";
            SavesPosition = true;
            Id = "ProximityChatWindow_com.floraaubry.gw2proximitychat_9a3f2c1e-5d8b-4e6a-b7c3-1f8e4a2d6c90";

            var generalIcon = TextureHelper.SolidColor(new Color(70, 130, 180));
            var debugIcon = TextureHelper.SolidColor(new Color(180, 90, 40));

            _generalTab = new Tab(generalIcon, CreateGeneralView, "General", 0);
            _debugTab   = new Tab(debugIcon, CreateDebugView, "Debug", 10);

            Tabs.Add(_generalTab);
            Tabs.Add(_debugTab);

            SelectedTab = _generalTab;
            Subtitle = _generalTab.Name;

            TabChanged += (s, e) => Subtitle = SelectedTab?.Name;
        }

        private IView CreateGeneralView()
        {
            return new GeneralSettingsView(
                _settings, _proximityService, _applyServerAddress,
                ContentWidth, ContentHeight,
                RegisterActiveGeneralView, UnregisterActiveGeneralView);
        }

        private IView CreateDebugView()
        {
            return new DebugInfoView(_proximityService, ContentWidth, ContentHeight, RegisterActiveDebugView, UnregisterActiveDebugView);
        }

        private void RegisterActiveGeneralView(GeneralSettingsView view) => _activeGeneralView = view;

        private void UnregisterActiveGeneralView(GeneralSettingsView view)
        {
            if (_activeGeneralView == view) _activeGeneralView = null;
        }

        private void RegisterActiveDebugView(DebugInfoView view) => _activeDebugView = view;

        private void UnregisterActiveDebugView(DebugInfoView view)
        {
            if (_activeDebugView == view) _activeDebugView = null;
        }

        public override void UpdateContainer(GameTime gameTime)
        {
            base.UpdateContainer(gameTime);

            _activeGeneralView?.Tick(gameTime);
            _activeDebugView?.Tick(gameTime);
        }
    }
}
