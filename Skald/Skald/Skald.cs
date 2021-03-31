using System.Reflection;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using HarmonyLib;
using fastJSON;
using UnityEngine;
using System.IO;

namespace Skald
{
    [BepInPlugin(PLUGIN_ID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class SkaldMod : BaseUnityPlugin
    {
        const string PLUGIN_ID = "net.kinghfb.valheim.skald";
        const string PLUGIN_NAME = "Skald";
        const string PLUGIN_VERSION = "1.0.0";

        const string DREAM_TEXTS = @"\dreams.json";
        const string RUNESTONE_TEXTS = @"\runestones.json";

        public static ConfigEntry<bool> modEnabled;

        protected static List<string> m_SkaldRunestoneTexts = new List<string>();
        protected static List<string> m_SkaldDreamTexts = new List<string>();

        internal void Awake()
        {
            modEnabled = Config.Bind<bool>("General", "Enabled", true, "Enable this mod");

            if (!modEnabled.Value)
            {
                return;
            }
            
            Logger.Log(LogLevel.Info, "Initializing Skald...");

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), PLUGIN_ID);
            InitializeSkaldTexts();

            Logger.Log(LogLevel.Info, "Skald initialized.");
        }

        private void InitializeSkaldTexts()
        {
            string libDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            Logger.Log(LogLevel.Info, "Loading dream texts...");
            m_SkaldDreamTexts = JSON.ToObject<List<string>>(File.ReadAllText(libDir + DREAM_TEXTS));

            Logger.Log(LogLevel.Info, "Loading runestone texts...");
            m_SkaldRunestoneTexts = JSON.ToObject<List<string>>(File.ReadAllText(libDir + RUNESTONE_TEXTS));
        }

        [HarmonyPatch(typeof(RuneStone), nameof(RuneStone.Interact))]
        public class RunestonePatch
        {
            private static bool m_SkaldRunestonesInitialized = false;

            public static void Prefix(ref List<RuneStone.RandomRuneText> ___m_randomTexts)
            {
                if (!m_SkaldRunestonesInitialized)
                {
                    foreach (string runestoneText in m_SkaldRunestoneTexts)
                    {
                        ___m_randomTexts.Add(new RuneStone.RandomRuneText {
                            m_text = runestoneText
                        });
                    }

                    m_SkaldRunestonesInitialized = true;
                }
            }
        }

        [HarmonyPatch(typeof(DreamTexts), nameof(DreamTexts.GetRandomDreamText))]
        public class DreamTextsPatch
        {
            private static bool m_SkaldDreamsInitialized = false;

            public static void Prefix(ref List<DreamTexts.DreamText> ___m_texts)
            {
                if (!m_SkaldDreamsInitialized)
                {
                    foreach (string dreamText in m_SkaldDreamTexts)
                    {
                        ___m_texts.Add(new DreamTexts.DreamText {
                            m_text = dreamText,
                            m_chanceToDream = 1.0f
                        });
                    }

                    m_SkaldDreamsInitialized = true;
                }
            }
        }
    }
}
