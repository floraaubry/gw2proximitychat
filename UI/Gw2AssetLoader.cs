using System.Threading.Tasks;
using Blish_HUD.Content;

namespace GW2ProximityChat
{
    public static class Gw2AssetLoader
    {
        /// <summary>
        /// StandardWindow measures padding from the background texture size.
        /// FromAssetId starts as a tiny placeholder -- wait until the real dat asset is in.
        /// </summary>
        public static async Task<AsyncTexture2D> LoadAsync(int assetId, int minWidth = 64)
        {
            AsyncTexture2D tex = AsyncTexture2D.FromAssetId(assetId);

            for (int i = 0; i < 80; i++)
            {
                if (tex.HasTexture && tex.Width >= minWidth)
                {
                    return tex;
                }

                await Task.Delay(50).ConfigureAwait(true);
            }

            return tex;
        }
    }
}
