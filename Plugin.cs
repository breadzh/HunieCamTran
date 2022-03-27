using BepInEx;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace HunieCamTran
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static Font TranslateFont;
        public static Dictionary<string, string> TranslatedDict;
        public static Dictionary<string, string> TranslationCache;

        public static HashSet<string> TranslatedText;
        public static HashSet<string> UntranslatedAll = new(StringComparer.OrdinalIgnoreCase);
        private static readonly string DictPath = @$"{Paths.PluginPath}\\HunieCamTran\\dict.dat";
        private static readonly string UntranslatedPath = @$"{Paths.PluginPath}\\HunieCamTran\untran.dat";
        private static readonly string disclaimer = @"
1.本汉化包出于学习与交流的目的制作，不得用于任何商业用途，否则保留追究一切责任的权利。
2.本汉化包非官方汉化程序，对于未正确使用汉化包而造成的任何损失，本汉化组不负任何责任。
3.您肯定已经详细阅读并已理解本协议，并同意严格遵守免责声明的各条款和条件。如不同意，请勿使用。
            ";
        private static readonly List<string> skipEndings = new() { "PM", "AM", "hr", "---" };
        private static bool EnableEasyGame = false;

        private static readonly List<Regex> skipRegexes = new()
        {
            new Regex("^\\d+/\\d+$"),
            new Regex("^\\d+%$"),
            new Regex("^[0-9.,]+M$"),
            new Regex("^[0-9.,]+K$"),
            new Regex("^\\d+H \\d+M$"),
            new Regex("^[A-F]$"),
        };

        private static readonly Regex[] RegexMatchPatterns = new Regex[]
        {
            // <color=#477F93FF>Style Lvl 1:</color> Earns 5 Fans each time she does a photo shoot.
            new Regex(@"<color=#477F93FF>(?<kind>\w+) Lvl (?<lvl>\d+):</color> (?<earn>Earns) (?<val>[0-9.]+) (?<desc>.*)"),
            
            // Tiffany is stress free!
            new Regex(@"(?<girl>\w+) (?<desc>is stress free!)"),

            // Nadia is now at Style Level 4!  ($16/hr)
            new Regex(@"(?<girl>\w+) (?<desc>is now at Style Level) .*"),

            // +10 cash
            new Regex(@"(?<diff>[-+][0-9,]+) (?<item>\w+)"),

            // Are you sure you want to overwrite file "A"?
            new Regex(@"(Are you sure you want to overwrite file) .*"),

            // Nikki has been recruited! 
            new Regex(@"(?<girl>\w+) (has been recruited!)"),

            // 2 DAYS LEFT!
            new Regex(@"(\d+) (DAYS LEFT!)"),

            // Aiko has caught Chlamydia!
            new Regex(@"(\w+) (has caught) (\w+)!"),

            // Aiko is too stressed out!
            new Regex(@"(?<girl>\w+) (is too stressed out!)"),
            
            // Aiko's talent is maxed out!
            new Regex(@"(?<girl>[\w']+) (talent is maxed out!)"),

            // Aiko's style is maxed out!
            new Regex(@"(?<girl>[\w']+) (style is maxed out!)"),

            // Audrey is now at Talent Level 3!  ($64/hr)
            new Regex(@"(?<girl>\w+) (?<desc>is now at Talent Level) .*"),

            // .* (Upgraded)!
            new Regex(@".* (Upgraded)!"),

            new Regex(@".* (out of business)!"),

            // Can't 护送 with an STD!
            new Regex(@"(Can't) .* with .*"),

            // 10 Hrs
            new Regex(@"(\d+) (\w+)"),
        };

        private readonly object lockobj = new();
        private bool showDisclaimer = true;

        private void Awake()
        {
            TranslationCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            TranslateFont = Font.CreateDynamicFontFromOSFont("Arial", 99);

            Logger.LogInfo("Loading dict");
            LoadDict();

            Logger.LogInfo("Patching methods");
            Harmony.CreateAndPatchAll(typeof(Plugin));

            Logger.LogInfo("Plugin init done");
        }

        private void Update()
        {
            if (Input.GetKey(KeyCode.Space))
            {
                lock(lockobj)
                {
                    LoadDict();

                    File.WriteAllLines(UntranslatedPath, UntranslatedAll.OrderBy(x => x).ToArray());
                    Console.WriteLine("saved");
                }
            } else if(Input.GetKey(KeyCode.J))
            {
                EnableEasyGame = true;
            }
        }

        private void OnDestroy()
        {
            Logger.LogInfo("Clean-up");
            Harmony.UnpatchAll();
        }

        private void OnGUI()
        {
            if (!showDisclaimer) return;

            Rect rect = new(Screen.width / 2 - 200, Screen.height / 2 - 150, 500, 200);
            rect = GUILayout.Window(999900, rect, id => {
                GUILayout.Label(disclaimer);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("同意"))
                {
                    showDisclaimer = false;
                }
            }, "免费声明", GUILayout.ExpandHeight(true));
        }

        private void LoadDict()
        {
            Logger.LogInfo($"{DictPath}");
            var lines = File.ReadAllLines(DictPath).Select(x => x.Split('\t'));
            TranslatedDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach(var line in lines)
            {
                var key = line[0];
                if(TranslatedDict.ContainsKey(key))
                {
                    Logger.LogWarning($"Dup key: {key}");
                } else if(line.Length > 1)
                {
                    TranslatedDict.Add(key, line[1]);
                }
            }

            TranslatedText = new(TranslatedDict.Values, StringComparer.OrdinalIgnoreCase);
        }

        private static bool ShouldSkipTranslation(string str)
        {
            if (string.IsNullOrEmpty(str)) return true;

            if (double.TryParse(str, out var _)) return true;

            if (str.Contains("<color")) return true;

            if (skipEndings.Any(x => str.EndsWith(x, StringComparison.OrdinalIgnoreCase))) return true;

            if (TranslatedText.Contains(str)) return true;

            if (skipRegexes.Any(x => x.IsMatch(str))) return true;

            return false;
        }

        private static bool TryTranslate(string text, out string translated, bool regex = false)
        {
            string val;
            if (TranslationCache.TryGetValue(text, out val))
            {
                translated = val;
                return true;
            }

            if (ShouldSkipTranslation(text))
            {
                TranslationCache.Add(text, text);
                translated = text;
                return true;
            }

            if (TranslatedDict.TryGetValue(text, out val))
            {
                TranslationCache.Add(text, val);
                translated = val;
                return true;
            }

            if (regex && TryTraslateByRegex(text, out val))
            {
                TranslationCache.Add(text, val);
                translated = val;
                return true;
            }

            if(!text.Contains("<color"))
            {
                Console.WriteLine($"Untranslated: {text}");
                UntranslatedAll.Add(text);
            }

            TranslationCache.Add(text, text);
            translated = null;
            return false;
        }

        private static bool TryTraslateByRegex(string text, out string translated)
        {
            translated = null;
            foreach(var reg in RegexMatchPatterns)
            {
                var matches = reg.Match(text);
                if (!matches.Success) continue;

                foreach (Group g in matches.Groups)
                {
                    if (translated == null)
                    {
                        translated = text;
                        continue;
                    }

                    if (!TryTranslate(g.Value, out var val)) continue;

                    translated = translated.Replace(g.Value, val);
                }

                return true;
            }

            return false;
        }

        private static void TranslateAsset()
        {
            VisitObject("Girls", Game.Data.Girls.GetAll(), patch: false);
            VisitObject("Locations", Game.Data.Locations.GetAll(), patch: false);
            VisitObject("Activities", Game.Data.Activities.GetAll(), patch: false);
            VisitObject("Fetishes", Game.Data.Fetishes.GetAll(), patch: true);
            VisitObject("Upgrades", Game.Data.Upgrades.GetAll(), patch: true);
            VisitObject("Accessories", Game.Data.Accessories.GetAll(), patch: true);
            VisitObject("Windows", Game.Data.Windows.GetAll(), patch: false);
            VisitObject("Tutorials", Game.Data.Tutorials.GetAll(), patch: true);
            VisitObject("Websites", Game.Data.Websites.GetAll(), patch: false);
            VisitObject("Diseases", Game.Data.Diseases.GetAll(), patch: false);
            VisitObject("Trophies", Game.Data.Trophies.GetAll(), patch: false);
            VisitObject("DebugProfiles", Game.Data.DebugProfiles.GetAll(), patch: false);
        }

        #region harmony patching

        [HarmonyPrefix, HarmonyPatch(typeof(Text), "text", MethodType.Setter)]
        public static void SetText(Text __instance, ref string value)
        {
            if (TryTranslate(value, out var translated, true))
            {
                value = translated;
            }
        }

        private static bool hasAssetTranslated = false;
        [HarmonyPrefix, HarmonyPatch(typeof(Text), "OnEnable")]
        public static void EnableText(Text __instance)
        {
            if(!hasAssetTranslated)
            {
                hasAssetTranslated = true;
                TranslateAsset();
            }

            // 覆盖字体
            if (__instance.font != TranslateFont)
            {
                __instance.font = TranslateFont;
            }

            // 翻译固定文字
            if (TryTranslate(__instance.text, out var translated, true))
            {
                __instance.text = translated;
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(LocationPlayerData), "ApplyResources")]
        public static void ApplyResources(LocationPlayerData __instance, int __result, ref int resourceValue)
        {
            if(EnableEasyGame)
            {
                resourceValue = 200000;
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PlayerManager), "GetUpgradeLevel", typeof(UpgradeType), typeof(int))]
        public static void GetUpgradeLevel(PlayerManager __instance, UpgradeLevelDefinition __result, UpgradeType upgradeType, int offset)
        {
            if(EnableEasyGame && upgradeType == UpgradeType.PRODUCTIVITY)
            {
                __result.levelValue = 1440;
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UiWardrobeWindow), "OnOutfitButtonPressed")]
        public static void OnOutfitButtonPressed()
        {
            Game.Persistence.saveData.tokens = 1000;
        }

        #endregion

        #region 反射遍历对象，用于收集和替换文本
        static readonly Type[] skipTypes = new Type[] { typeof(Sprite), typeof(Color), typeof(Vector2), typeof(Vector3), typeof(AudioGroup), typeof(float), typeof(int), typeof(bool) };

        static void VisitObject(string memberName, object obj, List<object> visit = null, bool patch = false)
        {
            if (obj == null) return;

            var type = obj.GetType();
            if (skipTypes.Contains(type) || type.IsEnum) return;

            visit ??= new();
            if (visit.Contains(obj)) return;

            visit.Add(obj);

            // str
            if (type == typeof(string))
            {
                return;
            }

            // expand lst
            if (obj is IList lst)
            {
                foreach (var x in lst)
                {
                    VisitObject($"{memberName}", x, visit, patch);
                }

                return;
            }

            // expand obj
            foreach (var member in type.GetMembers())
            {
                if (member.MemberType != System.Reflection.MemberTypes.Field) continue;

                var fieldInfo = (FieldInfo)member;
                VisitObject($"{type}\t{member.Name}", fieldInfo.GetValue(obj), visit, patch);

                if(patch && fieldInfo.FieldType == typeof(string))
                {
                    if(TranslatedDict.TryGetValue((string)fieldInfo.GetValue(obj), out var val))
                    {
                        fieldInfo.SetValue(obj, val);
                    }
                }
            }
        }

        #endregion
    }
}
