using System;
using System.Reflection;
using System.Net;
using System.Net.Sockets;
using System.Text;
using HarmonyLib;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace CampaignMod
{
    [BepInPlugin(pluginGuid, pluginName, pluginVerson)]
    [BepInProcess("Poly Bridge 2")]
    public class AutoSplitMain : BaseUnityPlugin
    {

        public const string pluginGuid = "polytech.autosplitmod";

        public const string pluginName = "Auto Split Mod";

        public const string pluginVerson = "1.0.1";

        public ConfigDefinition modEnableDef = new ConfigDefinition(pluginName, "Enable/Disable Mod");

        public ConfigDefinition SplitOnNextDef = new ConfigDefinition(pluginName, "Split on next level");

        public ConfigDefinition SplitOnCampaignDef = new ConfigDefinition(pluginName, "Split on next campaign");

        public ConfigDefinition SplitOnFlagDef = new ConfigDefinition(pluginName, "Split on level complete");

        public ConfigDefinition ResetOnMainMenuDef = new ConfigDefinition(pluginName, "Reset on main menu");

        public ConfigDefinition PortDef = new ConfigDefinition(pluginName, "Port");

        public ConfigDefinition IPDef = new ConfigDefinition(pluginName, "IP");

        public ConfigEntry<bool> mEnabled;

        public ConfigEntry<bool> mSplitOnNext;

        public ConfigEntry<bool> mSplitOnCampaign;

        public ConfigEntry<bool> mSplitOnFlag;

        public ConfigEntry<bool> mResetOnMainMenu;

        public ConfigEntry<int> mPort;

        public ConfigEntry<string> mIP;

        public Socket LivesplitSocket;

        public bool NextLevel = false;
        public bool NextCampaign = false;

        public static AutoSplitMain instance;

        void Awake()
        {
            if (instance == null) instance = this;

            int order = 0;

            Config.Bind(modEnableDef, true, new ConfigDescription("Controls if the mod should be enabled or disabled (can still be enabled if ptf is disabled)", null, new ConfigurationManagerAttributes { Order = order }));
            mEnabled = (ConfigEntry<bool>)Config[modEnableDef];
            order--;

            Config.Bind(SplitOnNextDef, true, new ConfigDescription("If enabled will split the timer when next level is clicked", null, new ConfigurationManagerAttributes { Order = order }));
            mSplitOnNext = (ConfigEntry<bool>)Config[SplitOnNextDef];
            order--;

            Config.Bind(SplitOnCampaignDef, true, new ConfigDescription("If enabled will split the timer when next level is clicked and its from a different world", null, new ConfigurationManagerAttributes { Order = order }));
            mSplitOnCampaign = (ConfigEntry<bool>)Config[SplitOnCampaignDef];
            order--;

            Config.Bind(SplitOnFlagDef, false, new ConfigDescription("If enabled will split the timer when all vehicles hit their flags", null, new ConfigurationManagerAttributes { Order = order }));
            mSplitOnFlag = (ConfigEntry<bool>)Config[SplitOnFlagDef];
            order--;

            Config.Bind(ResetOnMainMenuDef, true, new ConfigDescription("If enabled will reset the timer when you go back to the main menu", null, new ConfigurationManagerAttributes { Order = order }));
            mResetOnMainMenu = (ConfigEntry<bool>)Config[ResetOnMainMenuDef];
            order--;

            Config.Bind(PortDef, 16834, new ConfigDescription("What port the Live Split server is using", null, new ConfigurationManagerAttributes { Order = order }));
            mPort = (ConfigEntry<int>)Config[PortDef];
            order--;

            Config.Bind(IPDef, "127.0.0.1", new ConfigDescription("What the IP of the Live Split server is, use the default if the live split server is running on the same pc", null, new ConfigurationManagerAttributes { Order = order, IsAdvanced = true }));
            mIP = (ConfigEntry<string>)Config[IPDef];
            order--;

            Config.SettingChanged += onSettingChanged;
            onSettingChanged(null, null);

            Harmony harmony = new Harmony(pluginGuid);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        public void onSettingChanged(object sender, EventArgs e)
        {
            try
            {
                LivesplitSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                IPAddress ipAdd = IPAddress.Parse(mIP.Value);
                IPEndPoint remoteEP = new IPEndPoint(ipAdd, mPort.Value);
                if (!CheckForCheating()) LivesplitSocket.Connect(remoteEP);
            }
            catch
            {
                if(GameUI.m_Instance != null) PopUpWarning.Display("Could not connect to Live Split server using port: " + mPort.Value + "\nAre you using the right ip and port?");
                Debug.LogWarning("Could not connect to Live Split server using port: " + mPort.Value);
            }
        }

        private bool CheckForCheating()
        {
            return mEnabled.Value;
        }


        [HarmonyPatch(typeof(GameStateManager), "ChangeState")]
        private static class patchChangeState
        {
            private static void Postfix(GameState state)
            {
                if (!instance.CheckForCheating()) return;

                if (state != GameState.MAIN_MENU || !instance.mResetOnMainMenu.Value) return;
                instance.sendReset();
            }
        }

        [HarmonyPatch(typeof(Vehicle), "TouchedVictoryFlag")]
        private static class patchHitFlag
        {
            private static void Postfix()
            {
                if (!instance.CheckForCheating()) return;

                if (Vehicles.AllVehiclesHaveCollectedVictoryFlags() && instance.mSplitOnFlag.Value && GameStateManager.GetState() != GameState.MAIN_MENU) instance.sendStartOrSplit();
            }
        }

        [HarmonyPatch(typeof(Campaign), "LoadNextLevel")]
        private static class patchLoadNextLevel
        {
            private static void Prefix()
            {
                instance.NextCampaign = Campaign.m_CurrentLevel.m_WorldId != CampaignWorlds.m_Instance.GetNextLevel(Campaign.m_CurrentLevel).m_WorldId;

                instance.NextLevel = !instance.NextCampaign;
            }

            private static void Postfix()
            {
                instance.NextLevel = false;
                instance.NextCampaign = false;
            }
        }
        
        [HarmonyPatch(typeof(Campaign), "LoadLevel")]
        private static class patchLoadLevel
        {
            private static void Prefix()
            {
                if (!instance.CheckForCheating()) return;

                if (!instance.mSplitOnNext.Value && instance.NextLevel) return;
                if (!instance.mSplitOnCampaign.Value && instance.NextCampaign) return;
                instance.sendStartOrSplit();
            }
        }


        public void sendStartOrSplit()
        {
            if (!instance.LivesplitSocket.Connected)
            {
                if (GameUI.m_Instance != null) PopUpWarning.Display("Could not send start or split command to Live Split\nnot connected!");
                return;
            }
            byte[] byData = Encoding.ASCII.GetBytes("startorsplit\r\n");
            instance.LivesplitSocket.Send(byData);
        }

        public void sendReset()
        {
            if (!instance.LivesplitSocket.Connected)
            {
                if (GameUI.m_Instance != null) PopUpWarning.Display("Could not send reset command to Live Split\nnot connected!");
                return;
            }
            byte[] byData = Encoding.ASCII.GetBytes("reset\r\n");
            instance.LivesplitSocket.Send(byData);
        }
    }



    /// <summary>
    /// Class that specifies how a setting should be displayed inside the ConfigurationManager settings window.
    /// 
    /// Usage:
    /// This class template has to be copied inside the plugin's project and referenced by its code directly.
    /// make a new instance, assign any fields that you want to override, and pass it as a tag for your setting.
    /// 
    /// If a field is null (default), it will be ignored and won't change how the setting is displayed.
    /// If a field is non-null (you assigned a value to it), it will override default behavior.
    /// </summary>
    /// 
    /// <example> 
    /// Here's an example of overriding order of settings and marking one of the settings as advanced:
    /// <code>
    /// // Override IsAdvanced and Order
    /// Config.AddSetting("X", "1", 1, new ConfigDescription("", null, new ConfigurationManagerAttributes { IsAdvanced = true, Order = 3 }));
    /// // Override only Order, IsAdvanced stays as the default value assigned by ConfigManager
    /// Config.AddSetting("X", "2", 2, new ConfigDescription("", null, new ConfigurationManagerAttributes { Order = 1 }));
    /// Config.AddSetting("X", "3", 3, new ConfigDescription("", null, new ConfigurationManagerAttributes { Order = 2 }));
    /// </code>
    /// </example>
    /// 
    /// <remarks> 
    /// You can read more and see examples in the readme at https://github.com/BepInEx/BepInEx.ConfigurationManager
    /// You can optionally remove fields that you won't use from this class, it's the same as leaving them null.
    /// </remarks>
#pragma warning disable 0169, 0414, 0649
    internal sealed class ConfigurationManagerAttributes
    {
        /// <summary>
        /// Should the setting be shown as a percentage (only use with value range settings).
        /// </summary>
        public bool? ShowRangeAsPercent;

        /// <summary>
        /// Custom setting editor (OnGUI code that replaces the default editor provided by ConfigurationManager).
        /// See below for a deeper explanation. Using a custom drawer will cause many of the other fields to do nothing.
        /// </summary>
        public System.Action<BepInEx.Configuration.ConfigEntryBase> CustomDrawer;

        /// <summary>
        /// Show this setting in the settings screen at all? If false, don't show.
        /// </summary>
        public bool? Browsable;

        /// <summary>
        /// Category the setting is under. Null to be directly under the plugin.
        /// </summary>
        public string Category;

        /// <summary>
        /// If set, a "Default" button will be shown next to the setting to allow resetting to default.
        /// </summary>
        public object DefaultValue;

        /// <summary>
        /// Force the "Reset" button to not be displayed, even if a valid DefaultValue is available. 
        /// </summary>
        public bool? HideDefaultButton;

        /// <summary>
        /// Force the setting name to not be displayed. Should only be used with a <see cref="CustomDrawer"/> to get more space.
        /// Can be used together with <see cref="HideDefaultButton"/> to gain even more space.
        /// </summary>
        public bool? HideSettingName;

        /// <summary>
        /// Optional description shown when hovering over the setting.
        /// Not recommended, provide the description when creating the setting instead.
        /// </summary>
        public string Description;

        /// <summary>
        /// Name of the setting.
        /// </summary>
        public string DispName;

        /// <summary>
        /// Order of the setting on the settings list relative to other settings in a category.
        /// 0 by default, higher number is higher on the list.
        /// </summary>
        public int? Order;

        /// <summary>
        /// Only show the value, don't allow editing it.
        /// </summary>
        public bool? ReadOnly;

        /// <summary>
        /// If true, don't show the setting by default. User has to turn on showing advanced settings or search for it.
        /// </summary>
        public bool? IsAdvanced;

        /// <summary>
        /// Custom converter from setting type to string for the built-in editor textboxes.
        /// </summary>
        public System.Func<object, string> ObjToStr;

        /// <summary>
        /// Custom converter from string to setting type for the built-in editor textboxes.
        /// </summary>
        public System.Func<string, object> StrToObj;
    }
}
