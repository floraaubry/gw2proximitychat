using Blish_HUD.Input;
using Blish_HUD.Settings;
using Microsoft.Xna.Framework.Input;

namespace GW2ProximityChat
{
    /// <summary>
    /// Every persisted setting this module has, all defined in a subcollection with
    /// renderInUi=false -- nothing shows up in Blish HUD's native settings menu (sliders for
    /// every int, no way to render a dynamic device dropdown, etc.); everything is instead
    /// driven by real controls built into <see cref="ProximityChatWindow"/>.
    /// </summary>
    public class ProximityChatSettings
    {
        public SettingEntry<KeyBinding> ToggleWindowKeyBinding;
        public SettingEntry<KeyBinding> ToggleMicEnabledKeyBinding;
        public SettingEntry<KeyBinding> PushToTalkKeyBinding;

        public SettingEntry<string> ServerHost;
        public SettingEntry<string> ServerPort;
        public SettingEntry<string> ServerPassword;

        public SettingEntry<int> MicrophoneDeviceIndex;
        public SettingEntry<int> SpeakerDeviceIndex;

        public SettingEntry<float> InputVolume;
        public SettingEntry<float> OutputVolume;
        public SettingEntry<float> NoiseGateThreshold;
        public SettingEntry<int> ActivationMode;

        public static ProximityChatSettings Define(SettingCollection settings)
        {
            var hidden = settings.AddSubCollection("Internal (not shown in settings UI)");

            var s = new ProximityChatSettings
            {
                ToggleWindowKeyBinding = hidden.DefineSetting(
                    "ToggleWindowKeyBinding", new KeyBinding(ModifierKeys.Ctrl | ModifierKeys.Alt, Keys.M)),
                ToggleMicEnabledKeyBinding = hidden.DefineSetting(
                    "ToggleMicEnabledKeyBinding", new KeyBinding(ModifierKeys.Ctrl | ModifierKeys.Alt, Keys.N)),
                PushToTalkKeyBinding = hidden.DefineSetting(
                    "PushToTalkKeyBinding", new KeyBinding(ModifierKeys.Ctrl | ModifierKeys.Alt, Keys.V)),

                ServerHost = hidden.DefineSetting("ServerHost", "127.0.0.1"),
                ServerPort = hidden.DefineSetting("ServerPort", "5847"),
                ServerPassword = hidden.DefineSetting("ServerPassword", ""),

                MicrophoneDeviceIndex = hidden.DefineSetting("MicrophoneDeviceIndex", AudioDevices.SystemDefaultIndex),
                SpeakerDeviceIndex = hidden.DefineSetting("SpeakerDeviceIndex", AudioDevices.SystemDefaultIndex),

                InputVolume = hidden.DefineSetting("InputVolume", 1f),
                OutputVolume = hidden.DefineSetting("OutputVolume", 1f),
                NoiseGateThreshold = hidden.DefineSetting("NoiseGateThreshold", 0.02f),
                ActivationMode = hidden.DefineSetting("ActivationMode", (int)MicActivationMode.PushToTalk),
            };

            foreach (var keyBinding in new[] { s.ToggleWindowKeyBinding, s.ToggleMicEnabledKeyBinding, s.PushToTalkKeyBinding })
            {
                keyBinding.Value.Enabled = true;
                keyBinding.Value.BlockSequenceFromGw2 = true;
            }

            return s;
        }
    }
}
