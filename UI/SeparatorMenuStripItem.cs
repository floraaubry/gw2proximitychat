using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GW2ProximityChat
{
    /// <summary>
    /// ContextMenuStrip has no built-in separator item, so this is a disabled, unclickable
    /// ContextMenuStripItem with empty text that skips the base Paint entirely (no bullet, no
    /// label) and instead draws a thin horizontal line across its own width.
    /// </summary>
    public class SeparatorMenuStripItem : ContextMenuStripItem
    {
        private const int HorizontalPadding = 6;

        // ContextMenuStrip alternates each row's background before calling into item Paint, so
        // a bare line alone renders differently depending on whether it lands on an odd or even
        // row -- one separator ends up looking like a normal (enabled-looking) row. Painting an
        // opaque backdrop first neutralizes that alternating background so every separator looks
        // identical regardless of its position in the list.
        private static readonly AsyncTexture2D BackgroundTexture = TextureHelper.SolidColor(new Color(0, 0, 0, 160));
        private static readonly AsyncTexture2D LineTexture = TextureHelper.SolidColor(new Color(255, 255, 255, 60));

        public SeparatorMenuStripItem() : base(string.Empty)
        {
            Enabled = false;
            Height = 9;
        }

        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            spriteBatch.DrawOnCtrl(this, BackgroundTexture, new Rectangle(0, 0, Width, Height));
            spriteBatch.DrawOnCtrl(
                this,
                LineTexture,
                new Rectangle(HorizontalPadding, Height / 2, System.Math.Max(0, Width - HorizontalPadding * 2), 1));
        }
    }
}
