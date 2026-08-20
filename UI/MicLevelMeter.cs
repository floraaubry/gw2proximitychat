using System;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;

namespace GW2ProximityChat
{
    /// <summary>
    /// A small horizontal level meter (no native progress bar control exists in Blish HUD) built
    /// from two nested Panels, plus a thin marker showing where the noise gate threshold sits so
    /// input volume/gate can be calibrated visually while talking.
    /// </summary>
    public class MicLevelMeter
    {
        private readonly int _width;
        private readonly Panel _fill;
        private readonly Panel _thresholdMarker;

        public Panel Container { get; }

        public MicLevelMeter(Container parent, int width, int height)
        {
            _width = width;

            Container = new Panel
            {
                Width = width,
                Height = height,
                BackgroundColor = new Color(0, 0, 0, 150),
                Parent = parent,
            };

            _fill = new Panel
            {
                Width = 1,
                Height = height,
                BackgroundColor = Color.LimeGreen,
                Parent = Container,
            };

            // Cyan, not red -- the fill itself turns orange/red at high volume, so a red marker
            // here would read as "too loud" instead of "this is the gate threshold".
            _thresholdMarker = new Panel
            {
                Width = 3,
                Height = height,
                BackgroundColor = Color.Cyan,
                Parent = Container,
            };
        }

        public void SetLevel(float level01)
        {
            level01 = MathHelper.Clamp(level01, 0f, 1f);

            _fill.Width = Math.Max(1, (int)(level01 * _width));
            _fill.BackgroundColor = level01 >= 0.9f ? Color.OrangeRed : level01 >= 0.5f ? Color.Yellow : Color.LimeGreen;
        }

        public void SetThreshold(float threshold01)
        {
            threshold01 = MathHelper.Clamp(threshold01, 0f, 1f);
            _thresholdMarker.Left = (int)(threshold01 * _width);
        }
    }
}
