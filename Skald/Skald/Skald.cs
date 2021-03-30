using System.Reflection;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Skald
{
    [BepInPlugin(PLUGIN_ID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class SkaldMod : BaseUnityPlugin
    {
        const string PLUGIN_ID = "net.kinghfb.valheim.skald";
        const string PLUGIN_NAME = "Skald";
        const string PLUGIN_VERSION = "1.0.0";

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
            Logger.Log(LogLevel.Info, "Generating Skald texts...");
            
            for (int i = 0; i < 10; i++)
            {
                string rstxt = $"Lorem ipsum runestoner SKald dolor sit amet. {i}";
                Logger.Log(LogLevel.Info, $"added runestone text: {rstxt}");
                m_SkaldRunestoneTexts.Add(rstxt);

                string dreamtxt = $"Lorem ipsum dream SKald dolor sit amet. {i}";
                Logger.Log(LogLevel.Info, $"added dream text: {dreamtxt}");
                m_SkaldDreamTexts.Add(dreamtxt);
            }
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
