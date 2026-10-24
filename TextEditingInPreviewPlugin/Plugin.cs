using System;
using System.Reflection;
using HarmonyLib;
using YukkuriMovieMaker.Plugin;

namespace TextEditingInPreviewPlugin
{
    [PluginDetails(
        AuthorName = "Assistant",
        ContentId = "TextEditingInPreviewPlugin"
    )]
    public class Plugin : IPlugin
    {
        public string Name => "プレビュー上テキスト直接編集プラグイン";

        private static bool _isPatched = false;
        private static readonly object _lock = new object();

        public Plugin()
        {
            InitializeHarmony();
        }

        private static void InitializeHarmony()
        {
            if (_isPatched) return;

            lock (_lock)
            {
                if (_isPatched) return;

                try
                {
                    var harmony = new Harmony("com.ymm4.texteditinginpreview");
                    harmony.PatchAll(Assembly.GetExecutingAssembly());
                    _isPatched = true;
                    System.Diagnostics.Debug.WriteLine("[TextEditingInPreviewPlugin] Harmony patches successfully applied!");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[TextEditingInPreviewPlugin] Harmony patch failed: {ex}");
                }
            }
        }
    }
}
