using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using YukkuriMovieMaker.Controls;

using Application = System.Windows.Application;
using RichTextBox = System.Windows.Controls.RichTextBox;
using TextBox = System.Windows.Controls.TextBox;
using ContextMenu = System.Windows.Controls.ContextMenu;

namespace TextEditingInPreviewPlugin.Services
{
    public static class RichTextEditorFinder
    {
        private static Type? _commandsType;

        static RichTextEditorFinder()
        {
            _commandsType = typeof(YukkuriMovieMaker.Controls.RichTextEditor).Assembly
                .GetType("YukkuriMovieMaker.Controls.RichTextEditorCommands");
        }

        public static ICommand? GetCommand(string commandName)
        {
            if (_commandsType == null) return null;
            var prop = _commandsType.GetProperty(commandName, BindingFlags.Public | BindingFlags.Static);
            return prop?.GetValue(null) as ICommand;
        }

        /// <summary>
        /// アイテムウィンドウ（ItemEditorView / PropertiesEditor）内にある RichTextEditor を探す。
        /// 画面下部の「セリフ入力欄」を絶対に誤認識せず、選択中アイテムのテキスト入力欄を特定する。
        /// </summary>
        public static YukkuriMovieMaker.Controls.RichTextEditor? FindEditorForItem(object? targetItem)
        {
            if (Application.Current == null) return null;

            foreach (Window window in Application.Current.Windows)
            {
                // 優先度 1: ItemEditorView の配下にあり、targetItem にバインドされているもの
                foreach (var rte in FindVisualChildren<YukkuriMovieMaker.Controls.RichTextEditor>(window))
                {
                    if (!rte.IsVisible) continue;
                    if (!IsInsideItemEditor(rte)) continue;

                    if (targetItem != null)
                    {
                        var binding = BindingOperations.GetBindingExpression(rte, YukkuriMovieMaker.Controls.RichTextEditor.TextProperty);
                        if (binding?.DataItem == targetItem)
                        {
                            return rte;
                        }

                        var decBinding = BindingOperations.GetBindingExpression(rte, YukkuriMovieMaker.Controls.RichTextEditor.DecorationsProperty);
                        if (decBinding?.DataItem == targetItem)
                        {
                            return rte;
                        }
                    }
                }

                // 優先度 2: ItemEditorView の配下にある表示中の RichTextEditor
                foreach (var rte in FindVisualChildren<YukkuriMovieMaker.Controls.RichTextEditor>(window))
                {
                    if (rte.IsVisible && IsInsideItemEditor(rte))
                    {
                        return rte;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 指定された要素が「アイテムエディター（アイテムウィンドウ）」の配下にあるか判定する。
        /// セリフ入力欄（タイムライン側）を徹底除外する。
        /// </summary>
        public static bool IsInsideItemEditor(DependencyObject element)
        {
            var parent = VisualTreeHelper.GetParent(element);
            while (parent != null)
            {
                string typeName = parent.GetType().FullName ?? "";
                if (typeName.Contains("ItemEditor") || typeName.Contains("PropertiesEditor"))
                {
                    return true;
                }
                parent = VisualTreeHelper.GetParent(parent);
            }
            return false;
        }

        /// <summary>
        /// フォールバック: 表示中のアイテムウィンドウの RichTextEditor を返す
        /// </summary>
        public static YukkuriMovieMaker.Controls.RichTextEditor? FindActiveEditor()
        {
            return FindEditorForItem(null);
        }

        public static (TextBox? textBox, RichTextBox? richTextBox, ContextMenu? contextMenu) GetControls(
            YukkuriMovieMaker.Controls.RichTextEditor rte)
        {
            var tb = rte.Template?.FindName("PARTS_TextBox", rte) as TextBox;
            var rtb = rte.Template?.FindName("PARTS_RichTextBox", rte) as RichTextBox;

            ContextMenu? menu = null;
            if (rtb != null && rtb.Visibility == Visibility.Visible && rtb.ContextMenu != null)
            {
                menu = rtb.ContextMenu;
            }
            else if (tb != null && tb.Visibility == Visibility.Visible && tb.ContextMenu != null)
            {
                menu = tb.ContextMenu;
            }
            else
            {
                menu = rtb?.ContextMenu ?? tb?.ContextMenu;
            }

            return (tb, rtb, menu);
        }

        public static void SyncSelectionToRealEditor(
            YukkuriMovieMaker.Controls.RichTextEditor realRte, int selectStart, int selectLength)
        {
            try
            {
                var (tb, rtb, _) = GetControls(realRte);
                if (rtb != null && rtb.Visibility == Visibility.Visible)
                {
                    var pStart = GetTextPointerAtOffset(rtb.Document, selectStart);
                    var pEnd = GetTextPointerAtOffset(rtb.Document, selectStart + selectLength);
                    if (pStart != null && pEnd != null)
                    {
                        rtb.Selection.Select(pStart, pEnd);
                    }
                }
                else if (tb != null && tb.Visibility == Visibility.Visible)
                {
                    tb.Select(selectStart, selectLength);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SyncSelection Error] {ex}");
            }
        }

        public static (int start, int length) GetSelectionFromRealEditor(
            YukkuriMovieMaker.Controls.RichTextEditor realRte)
        {
            try
            {
                var (tb, rtb, _) = GetControls(realRte);
                if (rtb != null && rtb.Visibility == Visibility.Visible)
                {
                    int start = new TextRange(rtb.Document.ContentStart, rtb.Selection.Start).Text.Length;
                    int length = rtb.Selection.Text.Length;
                    return (start, length);
                }
                else if (tb != null && tb.Visibility == Visibility.Visible)
                {
                    return (tb.SelectionStart, tb.SelectionLength);
                }
            }
            catch { }
            return (0, 0);
        }

        private static TextPointer? GetTextPointerAtOffset(FlowDocument doc, int offset)
        {
            int current = 0;
            foreach (var para in FindVisualChildren<Paragraph>(doc))
            {
                var ptr = FindPointerInInlines(para.Inlines, offset, ref current);
                if (ptr != null) return ptr;
                current += 2; // 改行分
            }
            return doc.ContentEnd;
        }

        private static TextPointer? FindPointerInInlines(InlineCollection inlines, int offset, ref int current)
        {
            foreach (var inline in inlines)
            {
                if (inline is Run r)
                {
                    if (current <= offset && offset <= current + r.Text.Length)
                    {
                        return r.ContentStart.GetPositionAtOffset(offset - current);
                    }
                    current += r.Text.Length;
                }
                else if (inline is Span s)
                {
                    var ptr = FindPointerInInlines(s.Inlines, offset, ref current);
                    if (ptr != null) return ptr;
                }
                else if (inline is LineBreak)
                {
                    current += 2;
                }
            }
            return null;
        }

        public static IEnumerable<T> FindVisualChildren<T>(DependencyObject? depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;

            if (depObj is FlowDocument doc)
            {
                foreach (var block in doc.Blocks)
                {
                    if (block is T t) yield return t;
                }
                yield break;
            }

            int count = VisualTreeHelper.GetChildrenCount(depObj);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T typedChild)
                {
                    yield return typedChild;
                }

                foreach (var childOfChild in FindVisualChildren<T>(child))
                {
                    yield return childOfChild;
                }
            }
        }
    }
}
