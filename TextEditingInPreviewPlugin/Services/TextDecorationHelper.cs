using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;

using RichTextBox = System.Windows.Controls.RichTextBox;
using FontFamily = System.Windows.Media.FontFamily;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using FontStyle = System.Windows.FontStyle;

namespace TextEditingInPreviewPlugin.Services
{
    public static class TextDecorationHelper
    {
        public static void IncreaseFontSize(RichTextBox richTextBox, double step = 2.0)
        {
            ApplyToSelection(richTextBox, (range) =>
            {
                object current = range.GetPropertyValue(TextElement.FontSizeProperty);
                double currentSize = (current is double d) ? d : richTextBox.FontSize;
                double newSize = Math.Max(1.0, currentSize + step);
                range.ApplyPropertyValue(TextElement.FontSizeProperty, newSize);
            });
        }

        public static void DecreaseFontSize(RichTextBox richTextBox, double step = 2.0)
        {
            ApplyToSelection(richTextBox, (range) =>
            {
                object current = range.GetPropertyValue(TextElement.FontSizeProperty);
                double currentSize = (current is double d) ? d : richTextBox.FontSize;
                double newSize = Math.Max(1.0, currentSize - step);
                range.ApplyPropertyValue(TextElement.FontSizeProperty, newSize);
            });
        }

        public static void ToggleBold(RichTextBox richTextBox)
        {
            ApplyToSelection(richTextBox, (range) =>
            {
                object current = range.GetPropertyValue(TextElement.FontWeightProperty);
                FontWeight newWeight = (current is FontWeight w && w == FontWeights.Bold) ? FontWeights.Normal : FontWeights.Bold;
                range.ApplyPropertyValue(TextElement.FontWeightProperty, newWeight);
            });
        }

        public static void ToggleItalic(RichTextBox richTextBox)
        {
            ApplyToSelection(richTextBox, (range) =>
            {
                object current = range.GetPropertyValue(TextElement.FontStyleProperty);
                FontStyle newStyle = (current is FontStyle s && s == FontStyles.Italic) ? FontStyles.Normal : FontStyles.Italic;
                range.ApplyPropertyValue(TextElement.FontStyleProperty, newStyle);
            });
        }

        public static void ToggleUnderline(RichTextBox richTextBox)
        {
            ApplyToSelection(richTextBox, (range) =>
            {
                object current = range.GetPropertyValue(Inline.TextDecorationsProperty);
                bool hasUnderline = false;
                if (current is TextDecorationCollection coll)
                {
                    hasUnderline = coll.Any(td => td.Location == TextDecorationLocation.Underline);
                }

                if (hasUnderline)
                {
                    range.ApplyPropertyValue(Inline.TextDecorationsProperty, new TextDecorationCollection());
                }
                else
                {
                    range.ApplyPropertyValue(Inline.TextDecorationsProperty, System.Windows.TextDecorations.Underline);
                }
            });
        }

        public static void ToggleStrikethrough(RichTextBox richTextBox)
        {
            ApplyToSelection(richTextBox, (range) =>
            {
                object current = range.GetPropertyValue(Inline.TextDecorationsProperty);
                bool hasStrike = false;
                if (current is TextDecorationCollection coll)
                {
                    hasStrike = coll.Any(td => td.Location == TextDecorationLocation.Strikethrough);
                }

                if (hasStrike)
                {
                    range.ApplyPropertyValue(Inline.TextDecorationsProperty, new TextDecorationCollection());
                }
                else
                {
                    range.ApplyPropertyValue(Inline.TextDecorationsProperty, System.Windows.TextDecorations.Strikethrough);
                }
            });
        }

        public static void ResetStyle(RichTextBox richTextBox, FontFamily defaultFont, double defaultFontSize, Brush defaultForeground)
        {
            ApplyToSelection(richTextBox, (range) =>
            {
                range.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
                range.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);
                range.ApplyPropertyValue(TextElement.FontSizeProperty, defaultFontSize);
                range.ApplyPropertyValue(TextElement.ForegroundProperty, defaultForeground);
                range.ApplyPropertyValue(TextElement.FontFamilyProperty, defaultFont);
                range.ApplyPropertyValue(Inline.TextDecorationsProperty, new TextDecorationCollection());
            });
        }

        public static void ApplyFontAndColor(RichTextBox richTextBox, string fontName, Color color)
        {
            ApplyToSelection(richTextBox, (range) =>
            {
                if (!string.IsNullOrEmpty(fontName))
                {
                    range.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(fontName));
                }
                range.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(color));
            });
        }

        private static void ApplyToSelection(RichTextBox richTextBox, Action<TextRange> action)
        {
            var selection = richTextBox.Selection;
            var targetRange = selection.IsEmpty
                ? new TextRange(richTextBox.Document.ContentStart, richTextBox.Document.ContentEnd)
                : selection;

            richTextBox.BeginChange();
            try
            {
                action(targetRange);
            }
            finally
            {
                richTextBox.EndChange();
            }
        }

        /// <summary>
        /// FlowDocumentからプレーンテキストとYMM4のTextDecorationリストを再帰的に完全生成
        /// </summary>
        public static (string text, ImmutableList<YukkuriMovieMaker.Commons.TextDecoration> decorations) ExtractTextAndDecorations(
            FlowDocument document, double baseFontSize, FontFamily baseFont, Color baseColor)
        {
            var list = new List<YukkuriMovieMaker.Commons.TextDecoration>();
            var sb = new StringBuilder();
            bool isFirstParagraph = true;

            foreach (var block in document.Blocks)
            {
                if (block is Paragraph paragraph)
                {
                    if (isFirstParagraph)
                    {
                        isFirstParagraph = false;
                    }
                    else
                    {
                        string lb = "\r\n";
                        var lbDec = new YukkuriMovieMaker.Commons.TextDecoration(
                            sb.Length, lb.Length, false, false, 1.0, null, null, true);
                        sb.Append(lb);
                        list.Add(lbDec);
                    }

                    TraverseInlines(paragraph.Inlines, sb, list, baseFontSize, baseFont, baseColor);
                }
                else if (block is Section section)
                {
                    foreach (var subBlock in section.Blocks.OfType<Paragraph>())
                    {
                        if (isFirstParagraph)
                        {
                            isFirstParagraph = false;
                        }
                        else
                        {
                            string lb = "\r\n";
                            var lbDec = new YukkuriMovieMaker.Commons.TextDecoration(
                                sb.Length, lb.Length, false, false, 1.0, null, null, true);
                            sb.Append(lb);
                            list.Add(lbDec);
                        }
                        TraverseInlines(subBlock.Inlines, sb, list, baseFontSize, baseFont, baseColor);
                    }
                }
            }

            string resultText = sb.ToString();

            // 万が一インライン走査で空になってしまった場合のセーフティ（TextRangeからのフォールバック）
            if (string.IsNullOrEmpty(resultText))
            {
                try
                {
                    string rangeText = new TextRange(document.ContentStart, document.ContentEnd).Text;
                    // WPF TextRangeは末尾に \r\n を自動付与するため取り除く
                    if (rangeText.EndsWith("\r\n"))
                    {
                        rangeText = rangeText.Substring(0, rangeText.Length - 2);
                    }
                    if (!string.IsNullOrEmpty(rangeText))
                    {
                        resultText = rangeText;
                    }
                }
                catch { }
            }

            var immutable = (resultText.Length == 0 || !list.Any(x => x.HasDecoration))
                ? ImmutableList<YukkuriMovieMaker.Commons.TextDecoration>.Empty
                : ImmutableList.CreateRange(list);

            return (resultText, immutable);
        }

        private static void TraverseInlines(InlineCollection inlines, StringBuilder sb,
            List<YukkuriMovieMaker.Commons.TextDecoration> list, double baseFontSize, FontFamily baseFont, Color baseColor)
        {
            foreach (var inline in inlines)
            {
                if (inline is Run run)
                {
                    int start = sb.Length;
                    string runText = run.Text;
                    if (string.IsNullOrEmpty(runText)) continue;

                    int length = runText.Length;
                    bool isBold = run.FontWeight == FontWeights.Bold;
                    bool isItalic = run.FontStyle == FontStyles.Italic;
                    double scale = (baseFontSize > 0) ? (run.FontSize / baseFontSize) : 1.0;

                    string? font = (run.FontFamily != null && run.FontFamily.Source != baseFont.Source)
                        ? run.FontFamily.Source
                        : null;

                    Color? color = null;
                    if (run.Foreground is SolidColorBrush scb && scb.Color != baseColor && scb.Color != Colors.Transparent)
                    {
                        color = scb.Color;
                    }

                    bool isUnderline = run.TextDecorations != null && run.TextDecorations.Any(d => d.Location == TextDecorationLocation.Underline);
                    bool isStrike = run.TextDecorations != null && run.TextDecorations.Any(d => d.Location == TextDecorationLocation.Strikethrough);

                    var dec = new YukkuriMovieMaker.Commons.TextDecoration(
                        start, length, isBold, isItalic, scale, font, color,
                        IsLineBreak: false, 0.0, 0.0, false, false, null, 0.0, isStrike, isUnderline);

                    sb.Append(runText);
                    list.Add(dec);
                }
                else if (inline is Span span)
                {
                    // Span（太字、斜体、IME変換文字列など）の中を再帰的に走査！
                    TraverseInlines(span.Inlines, sb, list, baseFontSize, baseFont, baseColor);
                }
                else if (inline is LineBreak)
                {
                    string lb = "\r\n";
                    var lbDec = new YukkuriMovieMaker.Commons.TextDecoration(
                        sb.Length, lb.Length, false, false, 1.0, null, null, true);
                    sb.Append(lb);
                    list.Add(lbDec);
                }
            }
        }

        /// <summary>
        /// テキストと装飾情報からFlowDocumentを構築（isTransparent=true で文字を完全透明化し、選択ハイライトのみ表示）
        /// </summary>
        public static FlowDocument BuildFlowDocument(string text, ImmutableList<YukkuriMovieMaker.Commons.TextDecoration>? decorations,
            FontFamily defaultFont, double defaultFontSize, Color defaultColor, bool isTransparent = true)
        {
            var brush = isTransparent ? Brushes.Transparent : new SolidColorBrush(defaultColor);

            var doc = new FlowDocument
            {
                FontFamily = defaultFont,
                FontSize = defaultFontSize,
                Foreground = brush,
                PagePadding = new Thickness(0)
            };

            if (string.IsNullOrEmpty(text))
            {
                doc.Blocks.Add(new Paragraph());
                return doc;
            }

            if (decorations == null || decorations.IsEmpty)
            {
                string[] lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                foreach (var line in lines)
                {
                    var p = new Paragraph(new Run(line) { Foreground = brush }) { Margin = new Thickness(0) };
                    doc.Blocks.Add(p);
                }
                return doc;
            }

            Paragraph? currentPara = null;
            foreach (var dec in decorations)
            {
                if (currentPara == null || dec.IsLineBreak)
                {
                    currentPara = new Paragraph { Margin = new Thickness(0) };
                    doc.Blocks.Add(currentPara);
                }

                if (!dec.IsLineBreak)
                {
                    int start = dec.Start;
                    int len = Math.Min(text.Length - start, dec.Length);
                    if (start >= 0 && len > 0 && start + len <= text.Length)
                    {
                        string sub = text.Substring(start, len);
                        var run = new Run(sub)
                        {
                            FontWeight = dec.IsBold ? FontWeights.Bold : FontWeights.Normal,
                            FontStyle = dec.IsItalic ? FontStyles.Italic : FontStyles.Normal,
                            FontSize = Math.Max(1.0, defaultFontSize * dec.Scale),
                            FontFamily = !string.IsNullOrEmpty(dec.Font) ? new FontFamily(dec.Font) : defaultFont,
                            Foreground = isTransparent ? Brushes.Transparent : (dec.Foreground.HasValue ? new SolidColorBrush(dec.Foreground.Value) : brush)
                        };

                        if (dec.IsUnderline)
                        {
                            run.TextDecorations.Add(System.Windows.TextDecorations.Underline);
                        }
                        if (dec.IsStrikethrough)
                        {
                            run.TextDecorations.Add(System.Windows.TextDecorations.Strikethrough);
                        }

                        currentPara.Inlines.Add(run);
                    }
                }
            }

            if (doc.Blocks.Count == 0)
            {
                doc.Blocks.Add(new Paragraph());
            }

            return doc;
        }
    }
}
