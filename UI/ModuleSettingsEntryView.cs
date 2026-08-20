using System;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Microsoft.Xna.Framework;

namespace GW2ProximityChat
{
    /// <summary>
    /// Shown in Blish HUD's own "Manage Modules" area for this module. Every real setting lives
    /// in a hidden subcollection (see <see cref="ProximityChatSettings"/>) and is edited through
    /// <see cref="ProximityChatWindow"/> instead, so without this the module would have nothing
    /// there at all -- this single button keeps it discoverable from where players expect it.
    /// </summary>
    public class ModuleSettingsEntryView : View
    {
        private readonly Action _openWindow;

        public ModuleSettingsEntryView(Action openWindow)
        {
            _openWindow = openWindow;
        }

        protected override void Build(Container buildPanel)
        {
            const int width = 260;
            const int height = 30;

            var button = new StandardButton
            {
                Text = "Open GW2 Proximity Chat Settings",
                Width = width,
                Height = height,
                Location = new Point((buildPanel.Width - width) / 2, (buildPanel.Height - height) / 2),
                Parent = buildPanel,
            };
            button.Click += (s, e) => _openWindow();
        }
    }
}
