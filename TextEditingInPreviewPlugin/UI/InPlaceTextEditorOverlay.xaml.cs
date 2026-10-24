using System;
using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using TextEditingInPreviewPlugin.Services;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.UndoRedo;

using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using FontFamily = System.Windows.Media.FontFamily;
using Color = System.Windows.Media.Color;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using RichTextBox = System.Windows.Controls.RichTextBox;

namespace TextEditingInPreviewPlugin.UI
{
    public partial class InPlaceTextEditorOverlay : Popup
    {
        private readonly object _playerInstance;
        private readonly IVideoItem _targetItem;
        public IVideoItem TargetItem => _targetItem;
        private readonly TextItem? _textItem;
        private readonly VoiceItem? _voiceItem;
        private readonly Vector2[] _quad;
        private readonly DrawDescription? _desc;
        private readonly Vector2 _initialClickVideoPos;

        private bool _isClosing = false;
        private bool _isSyncing = false;
        private bool _isDragging = false;
        private int _dragStartOffset = 0;

        private FontFamily _baseFont = new FontFamily("Yu Gothic UI");
        private double _baseFontSize = 34.0;
        private Color _baseFontColor = Colors.White;

        private YukkuriMovieMaker.Controls.RichTextEditor? _realRte;
        private TextBox? _realTb;
        private RichTextBox? _realRtb;

        public InPlaceTextEditorOverlay(object playerInstance, IVideoItem targetItem, Vector2[] quad, DrawDescription? desc, Vector2 initialClickVideoPos)
        {
            InitializeComponent();

            _playerInstance = playerInstance;
            _targetItem = targetItem;
            _textItem = targetItem as TextItem;
            _voiceItem = targetItem as VoiceItem;
            _quad = quad;
            _desc = desc;
            _initialClickVideoPos = initialClickVideoPos;

            Opened += OnOpened;
            Closed += OnClosed;

            // マウス操作による文字範囲選択・クリック
            MainRichTextBox.PreviewMouseLeftButtonDown += MainRichTextBox_PreviewMouseLeftButtonDown;
            MainRichTextBox.PreviewMouseMove += MainRichTextBox_PreviewMouseMove;
            MainRichTextBox.PreviewMouseLeftButtonUp += MainRichTextBox_PreviewMouseLeftButtonUp;

            // 右クリックメニュー
            MainRichTextBox.MouseRightButtonUp += MainRichTextBox_MouseRightButtonUp;
            MainRichTextBox.ContextMenuOpening += MainRichTextBox_ContextMenuOpening;

            // キーボード操作
            MainRichTextBox.PreviewKeyDown += OnPreviewKeyDown;
            MainRichTextBox.PreviewTextInput += MainRichTextBox_PreviewTextInput;
        }

        private void OnOpened(object? sender, EventArgs e)
        {
            UpdateLayoutAndPosition();
            InitializeContent();
            EnsureRealEditor();

            // 初期キャレット位置をクリック位置に合わせる
            try
            {
                var clientPt = CoordinateConverter.VideoToClientPoint(_playerInstance, _initialClickVideoPos);
                var screenPt = CoordinateConverter.ClientToScreenPoint(_playerInstance, clientPt);
                var boxPt = MainRichTextBox.PointFromScreen(screenPt);

                var targetPos = MainRichTextBox.GetPositionFromPoint(boxPt, snapToText: true);
                if (targetPos != null)
                {
                    int offset = GetTextOffset(MainRichTextBox.Document, targetPos);
                    SyncSelectionToRealEditor(offset, 0);
                }
                else
                {
                    int offset = (_textItem?.Text?.Length ?? _voiceItem?.Serif?.Length ?? 0);
                    SyncSelectionToRealEditor(offset, 0);
                }
            }
            catch
            {
                int offset = (_textItem?.Text?.Length ?? _voiceItem?.Serif?.Length ?? 0);
                SyncSelectionToRealEditor(offset, 0);
            }

            // 本体のテキスト入力欄にフォーカスを当てる
            _realRte?.SetFocus();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _isClosing = true;
            DetachRealEditorEvents();
            Commit();
        }

        private void EnsureRealEditor()
        {
            if (_realRte != null) return;
            AttachRealEditorEvents();
        }

        private void AttachRealEditorEvents()
        {
            // アイテムウィンドウ（ItemEditorView / PropertiesEditor）内にある RichTextEditor を探す
            _realRte = RichTextEditorFinder.FindEditorForItem((object?)_textItem ?? _voiceItem);
            if (_realRte == null) return;

            var (tb, rtb, _) = RichTextEditorFinder.GetControls(_realRte);
            _realTb = tb;
            _realRtb = rtb;

            if (_realTb != null)
            {
                _realTb.SelectionChanged -= RealEditor_SelectionChanged;
                _realTb.SelectionChanged += RealEditor_SelectionChanged;
                _realTb.TextChanged -= RealEditor_TextChanged;
                _realTb.TextChanged += RealEditor_TextChanged;
            }
            if (_realRtb != null)
            {
                _realRtb.SelectionChanged -= RealEditor_SelectionChanged;
                _realRtb.SelectionChanged += RealEditor_SelectionChanged;
                _realRtb.TextChanged -= RealEditor_TextChanged;
                _realRtb.TextChanged += RealEditor_TextChanged;
            }
        }

        private void DetachRealEditorEvents()
        {
            if (_realTb != null)
            {
                _realTb.SelectionChanged -= RealEditor_SelectionChanged;
                _realTb.TextChanged -= RealEditor_TextChanged;
            }
            if (_realRtb != null)
            {
                _realRtb.SelectionChanged -= RealEditor_SelectionChanged;
                _realRtb.TextChanged -= RealEditor_TextChanged;
            }
        }

        private void RealEditor_SelectionChanged(object? sender, RoutedEventArgs e)
        {
            if (_isSyncing || _isClosing || _realRte == null) return;
            _isSyncing = true;
            try
            {
                var (start, length) = RichTextEditorFinder.GetSelectionFromRealEditor(_realRte);
                var pStart = GetTextPointerFromOffset(start);
                var pEnd = GetTextPointerFromOffset(start + length);
                if (pStart != null && pEnd != null)
                {
                    MainRichTextBox.Selection.Select(pStart, pEnd);
                }
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private void RealEditor_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_isSyncing || _isClosing) return;
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                ReloadFromItem();
            });
        }

        private void MainRichTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var pt = e.GetPosition(MainRichTextBox);
            var ptr = MainRichTextBox.GetPositionFromPoint(pt, snapToText: true);
            if (ptr != null)
            {
                _isDragging = true;
                MainRichTextBox.CaptureMouse();
                _dragStartOffset = GetTextOffset(MainRichTextBox.Document, ptr);
                SyncSelectionToRealEditor(_dragStartOffset, 0);
            }
        }

        private void MainRichTextBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                var pt = e.GetPosition(MainRichTextBox);
                var ptr = MainRichTextBox.GetPositionFromPoint(pt, snapToText: true);
                if (ptr != null)
                {
                    int currentOffset = GetTextOffset(MainRichTextBox.Document, ptr);
                    int start = Math.Min(_dragStartOffset, currentOffset);
                    int len = Math.Abs(currentOffset - _dragStartOffset);
                    SyncSelectionToRealEditor(start, len);
                }
            }
        }

        private void MainRichTextBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                MainRichTextBox.ReleaseMouseCapture();

                EnsureRealEditor();
                _realRte?.SetFocus();
            }
        }

        private void SyncSelectionToRealEditor(int start, int length)
        {
            // 1. プレビュー上の選択範囲（ハイライト）を確実に更新
            var pStart = GetTextPointerFromOffset(start);
            var pEnd = GetTextPointerFromOffset(start + length);
            if (pStart != null && pEnd != null)
            {
                MainRichTextBox.Selection.Select(pStart, pEnd);
            }

            // 2. 本体のテキスト入力欄に同期
            EnsureRealEditor();
            if (_realRte != null && !_isSyncing)
            {
                _isSyncing = true;
                try
                {
                    RichTextEditorFinder.SyncSelectionToRealEditor(_realRte, start, length);
                }
                finally
                {
                    _isSyncing = false;
                }
            }
        }

        private void InitializeContent()
        {
            string text = "";
            ImmutableList<YukkuriMovieMaker.Commons.TextDecoration>? decorations = null;

            if (_textItem != null)
            {
                text = _textItem.Text ?? "";
                decorations = _textItem.Decorations;
                if (!string.IsNullOrEmpty(_textItem.Font))
                {
                    _baseFont = new FontFamily(_textItem.Font);
                }
                _baseFontSize = _textItem.FontSize.Values.Count > 0 ? _textItem.FontSize.Values[0].Value : 34.0;
                _baseFontColor = _textItem.FontColor;
            }
            else if (_voiceItem != null)
            {
                text = _voiceItem.Serif ?? "";
                if (!string.IsNullOrEmpty(_voiceItem.Font))
                {
                    _baseFont = new FontFamily(_voiceItem.Font);
                }
                _baseFontSize = _voiceItem.FontSize.Values.Count > 0 ? _voiceItem.FontSize.Values[0].Value : 34.0;
                _baseFontColor = _voiceItem.FontColor;
            }

            float screenZoom = CoordinateConverter.GetPreviewScreenZoom(_playerInstance);
            double visualFontSize = Math.Max(12.0, _baseFontSize * screenZoom);

            // 文字は完全透明（isTransparent: true）で構築！
            MainRichTextBox.Document = TextDecorationHelper.BuildFlowDocument(
                text, decorations, _baseFont, visualFontSize, _baseFontColor, isTransparent: true);

            MainRichTextBox.FontFamily = _baseFont;
            MainRichTextBox.FontSize = visualFontSize;
            MainRichTextBox.Foreground = Brushes.Transparent;
        }

        public void UpdateLayoutAndPosition()
        {
            if (_quad == null || _quad.Length < 4) return;

            var p0 = CoordinateConverter.ClientToScreenPoint(_playerInstance, CoordinateConverter.VideoToClientPoint(_playerInstance, _quad[0]));
            var p1 = CoordinateConverter.ClientToScreenPoint(_playerInstance, CoordinateConverter.VideoToClientPoint(_playerInstance, _quad[1]));
            var p2 = CoordinateConverter.ClientToScreenPoint(_playerInstance, CoordinateConverter.VideoToClientPoint(_playerInstance, _quad[2]));
            var p3 = CoordinateConverter.ClientToScreenPoint(_playerInstance, CoordinateConverter.VideoToClientPoint(_playerInstance, _quad[3]));

            double minX = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
            double maxX = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
            double minY = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
            double maxY = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));

            double boxWidth = Math.Max(40, (maxX - minX) + 6);
            double boxHeight = Math.Max(24, (maxY - minY) + 4);

            Placement = PlacementMode.Absolute;
            HorizontalOffset = minX - 3;
            VerticalOffset = minY - 2;
            Width = boxWidth;
            Height = boxHeight;

            EditorBorder.Width = boxWidth;
            EditorBorder.Height = boxHeight;
            MainRichTextBox.Width = boxWidth;
            MainRichTextBox.Height = boxHeight;

            if (_desc != null && Math.Abs(_desc.Rotation.Z) > 0.01f)
            {
                EditorBorder.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
                EditorBorder.RenderTransform = new RotateTransform(_desc.Rotation.Z);
            }
            else
            {
                EditorBorder.RenderTransform = null;
            }
        }

        private void ReloadFromItem()
        {
            _isSyncing = true;
            try
            {
                string text = _textItem?.Text ?? _voiceItem?.Serif ?? "";
                var decorations = _textItem?.Decorations;
                float screenZoom = CoordinateConverter.GetPreviewScreenZoom(_playerInstance);
                double visualFontSize = Math.Max(12.0, _baseFontSize * screenZoom);

                MainRichTextBox.Document = TextDecorationHelper.BuildFlowDocument(
                    text, decorations, _baseFont, visualFontSize, _baseFontColor, isTransparent: true);

                EnsureRealEditor();
                if (_realRte != null)
                {
                    var (selStart, selLen) = RichTextEditorFinder.GetSelectionFromRealEditor(_realRte);
                    var pStart = GetTextPointerFromOffset(selStart);
                    var pEnd = GetTextPointerFromOffset(selStart + selLen);
                    if (pStart != null && pEnd != null)
                    {
                        MainRichTextBox.Selection.Select(pStart, pEnd);
                    }
                }
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private void MainRichTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            EnsureRealEditor();
            if (string.IsNullOrEmpty(e.Text) || _realRte == null) return;

            e.Handled = true;

            var (tb, rtb, _) = RichTextEditorFinder.GetControls(_realRte);
            if (rtb != null && rtb.Visibility == Visibility.Visible)
            {
                rtb.Focus();
                rtb.Selection.Text = e.Text;
            }
            else if (tb != null && tb.Visibility == Visibility.Visible)
            {
                tb.Focus();
                tb.SelectedText = e.Text;
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CommitAndClose();
                e.Handled = true;
                return;
            }

            EnsureRealEditor();

            bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (isCtrl && _realRte != null)
            {
                string? cmdName = e.Key switch
                {
                    Key.OemPeriod => "IncreaseFontSize",
                    Key.OemComma => "DecreaseFontSize",
                    Key.B => "ToggleBold",
                    Key.I => "ToggleItalic",
                    Key.U => "ToggleUnderline",
                    Key.D => "ToggleStrikethrough",
                    Key.F => "ChangeFontAndColor",
                    Key.R => "ResetStyle",
                    _ => null
                };

                if (cmdName != null)
                {
                    var cmd = RichTextEditorFinder.GetCommand(cmdName);
                    if (cmd != null)
                    {
                        var (tb, rtb, _) = RichTextEditorFinder.GetControls(_realRte);
                        var target = (rtb != null && rtb.Visibility == Visibility.Visible ? (IInputElement)rtb : tb) ?? _realRte;

                        if (cmd is RoutedCommand routedCmd)
                        {
                            if (routedCmd.CanExecute(null, target))
                            {
                                routedCmd.Execute(null, target);
                                e.Handled = true;
                                return;
                            }
                        }
                    }
                }
            }

            // BackSpace / Delete / Enter は本体へ送る
            if (_realRte != null)
            {
                var (tb, rtb, _) = RichTextEditorFinder.GetControls(_realRte);
                if (e.Key == Key.Back)
                {
                    e.Handled = true;
                    if (rtb != null && rtb.Visibility == Visibility.Visible)
                    {
                        rtb.Focus();
                        if (rtb.Selection.IsEmpty)
                        {
                            var prev = rtb.Selection.Start.GetNextInsertionPosition(LogicalDirection.Backward);
                            if (prev != null) new TextRange(prev, rtb.Selection.Start).Text = "";
                        }
                        else
                        {
                            rtb.Selection.Text = "";
                        }
                    }
                    else if (tb != null && tb.Visibility == Visibility.Visible)
                    {
                        tb.Focus();
                        if (tb.SelectionLength > 0)
                        {
                            tb.SelectedText = "";
                        }
                        else if (tb.SelectionStart > 0)
                        {
                            int idx = tb.SelectionStart - 1;
                            tb.Text = tb.Text.Remove(idx, 1);
                            tb.SelectionStart = idx;
                        }
                    }
                    return;
                }
                else if (e.Key == Key.Delete)
                {
                    e.Handled = true;
                    if (rtb != null && rtb.Visibility == Visibility.Visible)
                    {
                        rtb.Focus();
                        if (rtb.Selection.IsEmpty)
                        {
                            var next = rtb.Selection.Start.GetNextInsertionPosition(LogicalDirection.Forward);
                            if (next != null) new TextRange(rtb.Selection.Start, next).Text = "";
                        }
                        else
                        {
                            rtb.Selection.Text = "";
                        }
                    }
                    else if (tb != null && tb.Visibility == Visibility.Visible)
                    {
                        tb.Focus();
                        if (tb.SelectionLength > 0)
                        {
                            tb.SelectedText = "";
                        }
                        else if (tb.SelectionStart < tb.Text.Length)
                        {
                            int idx = tb.SelectionStart;
                            tb.Text = tb.Text.Remove(idx, 1);
                            tb.SelectionStart = idx;
                        }
                    }
                    return;
                }
                else if (e.Key == Key.Return)
                {
                    e.Handled = true;
                    if (rtb != null && rtb.Visibility == Visibility.Visible)
                    {
                        rtb.Focus();
                        rtb.Selection.Text = "\r\n";
                    }
                    else if (tb != null && tb.Visibility == Visibility.Visible)
                    {
                        tb.Focus();
                        tb.SelectedText = "\r\n";
                    }
                    return;
                }
            }

            _realRte?.SetFocus();
        }

        private void MainRichTextBox_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ShowRealEditorContextMenu();
        }

        private void MainRichTextBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            e.Handled = true;
            ShowRealEditorContextMenu();
        }

        private void ShowRealEditorContextMenu()
        {
            try
            {
                EnsureRealEditor();
                if (_realRte == null) return;

                var (tb, rtb, menu) = RichTextEditorFinder.GetControls(_realRte);
                if (menu != null)
                {
                    menu.Placement = PlacementMode.MousePoint;
                    menu.PlacementTarget = (rtb != null && rtb.Visibility == Visibility.Visible ? (UIElement)rtb : tb) ?? _realRte;
                    menu.IsOpen = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShowRealContextMenu Error] {ex}");
            }
        }

        private TextPointer? GetTextPointerFromOffset(int offset)
        {
            int current = 0;
            foreach (var block in MainRichTextBox.Document.Blocks)
            {
                if (block is Paragraph p)
                {
                    var ptr = FindPointerInInlines(p.Inlines, offset, ref current);
                    if (ptr != null) return ptr;
                    current += 2;
                }
            }
            return MainRichTextBox.Document.ContentEnd;
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

        private static int GetTextOffset(FlowDocument doc, TextPointer pointer)
        {
            if (pointer == null) return 0;
            try
            {
                return new TextRange(doc.ContentStart, pointer).Text.Length;
            }
            catch
            {
                return 0;
            }
        }

        public void CommitAndClose()
        {
            if (_isClosing) return;
            _isClosing = true;
            IsOpen = false;
        }

        private void Commit()
        {
            try
            {
                var timeline = CoordinateConverter.GetTimeline(_playerInstance);
                if (timeline != null)
                {
                    var undoProp = _playerInstance.GetType().GetProperty("UndoRedoManager", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var undo = undoProp?.GetValue(_playerInstance) as UndoRedoManager;
                    undo?.Record();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Commit Error] {ex}");
            }
        }
    }
}
