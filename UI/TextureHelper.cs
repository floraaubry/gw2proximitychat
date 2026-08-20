using Blish_HUD;
using Blish_HUD.Content;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GW2ProximityChat
{
    /// <summary>
    /// We don't have bundled icon art for things like tab icons, and guessing a game asset id
    /// for an icon can't be verified the way a compile can -- a wrong-but-valid id just silently
    /// renders the wrong picture. A small solid-color texture is a safe, honest placeholder.
    /// </summary>
    public static class TextureHelper
    {
        public static AsyncTexture2D SolidColor(Color color)
        {
            using (var context = GameService.Graphics.LendGraphicsDeviceContext())
            {
                var texture = new Texture2D(context.GraphicsDevice, 1, 1);
                texture.SetData(new[] { color });
                return new AsyncTexture2D(texture);
            }
        }
    }
}
