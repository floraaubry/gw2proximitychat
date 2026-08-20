using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GW2ProximityChat
{
    /// <summary>
    /// A ContextMenuStripItem whose leading square is recolored to reflect connection
    /// status, mirroring the corner icon's red/orange/green dots. ContextMenuStripItem's own
    /// bullet (drawn when CanCheck is false) is a fixed gray with no public hook to recolor
    /// it, so this overrides Paint to draw the base item first, then a solid-color square on
    /// top of that same bullet position. HorizontalPadding/BulletSize below are copies of
    /// ContextMenuStripItem's own private layout constants (18/6) -- they have to stay in
    /// sync since there's no way to read the base class's private values directly.
    /// </summary>
    public class StatusMenuStripItem : ContextMenuStripItem
    {
        private const int BulletSize = 18;
        private const int HorizontalPadding = 6;

        // The real bullet glyph is a small dot with a lot of transparent padding inside its
        // 18x18 draw rect -- filling that whole rect edge-to-edge looked oversized, so this
        // draws smaller and centered within the same rect instead.
        private const int IndicatorSize = 10;

        private Color _indicatorColor = Color.Gray;
        private AsyncTexture2D _indicatorTexture;

        public Color IndicatorColor
        {
            get => _indicatorColor;
            set
            {
                if (_indicatorColor == value) return;
                _indicatorColor = value;
                _indicatorTexture = TextureHelper.SolidColor(value);
            }
        }

        public StatusMenuStripItem(string itemText) : base(itemText)
        {
            _indicatorTexture = TextureHelper.SolidColor(_indicatorColor);
        }

        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            base.Paint(spriteBatch, bounds);

            spriteBatch.DrawOnCtrl(
                this,
                _indicatorTexture,
                new Rectangle(
                    HorizontalPadding + (BulletSize - IndicatorSize) / 2,
                    Height / 2 - IndicatorSize / 2,
                    IndicatorSize,
                    IndicatorSize));
        }
    }
}
