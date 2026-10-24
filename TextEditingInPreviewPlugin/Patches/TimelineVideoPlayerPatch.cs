using System;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Windows.Forms;
using HarmonyLib;
using TextEditingInPreviewPlugin.Models;
using TextEditingInPreviewPlugin.Services;
using TextEditingInPreviewPlugin.UI;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project.Items;

namespace TextEditingInPreviewPlugin.Patches
{
    [HarmonyPatch]
    public static class TimelineVideoPlayerPatch
    {
        private static readonly ClickTracker Tracker = new ClickTracker();
        private static FloatingTextEditorWindow? _activeWindow;

        private static Assembly? GetYmmAssembly()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "YukkuriMovieMaker")
                ?? Assembly.Load("YukkuriMovieMaker");
        }

        [HarmonyTargetMethod]
        public static MethodBase? TargetMethod()
        {
            var asm = GetYmmAssembly();
            var type = asm?.GetType("YukkuriMovieMaker.Player.TimelineVideoPlayer");
            return type?.GetMethod("Child_MouseDown", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        [HarmonyPrefix]
        public static bool Prefix(object __instance, MouseEventArgs e)
        {
            try
            {
                // 左クリック以外は無視
                if (e.Button != MouseButtons.Left)
                {
                    return true;
                }

                // ダブルクリック判定
                bool isDoubleClick = Tracker.IsDoubleClick(e);
                if (!isDoubleClick)
                {
                    return true;
                }

                // マウス位置（ビデオ座標）の取得
                var type = __instance.GetType();
                var getMousePosMethod = type.GetMethod("GetPreviewAreaMousePosition", BindingFlags.Instance | BindingFlags.NonPublic);
                if (getMousePosMethod == null) return true;

                Vector2 mouseVideoPos = (Vector2)getMousePosMethod.Invoke(__instance, null)!;

                // マウス位置にあるアイテムのヒットテスト
                var (item, quad, desc) = CoordinateConverter.FindItemAtPosition(__instance, mouseVideoPos);

                if (item != null && (item is TextItem || item is VoiceItem) && quad != null)
                {
                    // タイムライン上で選択状態にする
                    var timeline = CoordinateConverter.GetTimeline(__instance);
                    timeline?.SelectItem(item);

                    // クリック位置のスクリーン座標を計算
                    var clientPt = CoordinateConverter.VideoToClientPoint(__instance, mouseVideoPos);
                    var screenPt = CoordinateConverter.ClientToScreenPoint(__instance, clientPt);

                    // フローティング・テキストエディタウィンドウをWPFスレッドで起動
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            if (_activeWindow != null && _activeWindow.IsLoaded)
                            {
                                _activeWindow.Close();
                            }

                            _activeWindow = new FloatingTextEditorWindow(__instance, item, screenPt);
                            _activeWindow.Closed += (s, ev) => { _activeWindow = null; };
                            _activeWindow.Show();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Show FloatingWindow Error] {ex}");
                        }
                    });

                    // ダブルクリック時のドラッグ移動開始をキャンセル
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TimelineVideoPlayerPatch Error] {ex}");
            }

            return true;
        }

        [HarmonyPatch]
        public static class MouseMovePatch
        {
            [HarmonyTargetMethod]
            public static MethodBase? TargetMethod()
            {
                var asm = GetYmmAssembly();
                var type = asm?.GetType("YukkuriMovieMaker.Player.TimelineVideoPlayer");
                return type?.GetMethod("Child_MouseMove", BindingFlags.Instance | BindingFlags.NonPublic);
            }

            [HarmonyPrefix]
            public static bool Prefix(object __instance, MouseEventArgs e)
            {
                // テキスト編集ウィンドウが開いている間は、アイテムのドラッグ移動を抑止
                if (_activeWindow != null && _activeWindow.IsLoaded)
                {
                    if (e.Button == MouseButtons.Left)
                    {
                        return false;
                    }
                }
                return true;
            }
        }
    }
}
