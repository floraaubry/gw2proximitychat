using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;

namespace GW2ProximityChat
{
    /// <summary>
    /// Roster window: every peer currently in the relay, each with its own volume slider and
    /// mute button. Plain StandardWindow, no tabs -- there's only ever one thing to show here,
    /// unlike ProximityChatWindow's General/Debug split.
    ///
    /// Background/crop/windowSize copied verbatim from Thryphore/WvWPipTally's PipTallyWindow --
    /// a shipped mod using the exact same asset id (155985, loaded via Gw2AssetLoader), same crop
    /// rectangles, same proven-correct scale-down window size. Every previous version of this file
    /// tried to derive these numbers (from a bundled window_bg.png, or from hand-reversed
    /// WindowBase2 padding/ratio math) and got the sizing wrong every time; this asset+numbers
    /// combination is empirically known to render correctly, so it's reused as-is rather than
    /// re-derived again.
    /// </summary>
    public class UsersWindow : StandardWindow
    {
        private static readonly Rectangle BgWindowRegion  = new Rectangle(40, 26, 913, 691);
        private static readonly Rectangle BgContentRegion = new Rectangle(70, 36, 839, 605);

        private readonly ProximityService _proximityService;

        private readonly UsersListView _listView;

        public UsersWindow(AsyncTexture2D background, AsyncTexture2D talkingIcon, ProximityService proximityService)
            : base(
                background,
                BgWindowRegion,
                BgContentRegion,
                new Point(420, 415)) // back to PipTallyWindow's proven size -- the +20/+20 tried earlier just sat unused once the row content got leaner (label removed, slider trimmed)
        {
            _proximityService = proximityService;

            Parent = GameService.Graphics.SpriteScreen;
            Title = "GW2 Proximity Chat";
            Subtitle = "Users";
            SavesPosition = true;
            Id = "UsersWindow_com.floraaubry.gw2proximitychat_6e2b9a41-3c7d-4f1a-9e5b-2a8d6f4c1b30";

            _listView = new UsersListView(_proximityService, talkingIcon, ContentRegion.Width, ContentRegion.Height, this);
        }

        public override void UpdateContainer(GameTime gameTime)
        {
            base.UpdateContainer(gameTime);
            _listView.Tick(gameTime);
        }
    }
}
