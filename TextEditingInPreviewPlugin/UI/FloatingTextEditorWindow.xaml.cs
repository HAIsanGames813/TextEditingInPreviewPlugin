using System;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using TextEditingInPreviewPlugin.Services;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.UndoRedo;

using Point = System.Windows.Point;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Binding = System.Windows.Data.Binding;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace TextEditingInPreviewPlugin.UI
{
    public partial class FloatingTextEditorWindow : Window
    {
        private readonly object _playerInstance;
        private readonly IVideoItem _targetItem;
        private readonly TextItem? _textItem;
        private readonly VoiceItem? _voiceItem;

        public IVideoItem TargetItem => _targetItem;

        private YukkuriMovieMaker.Controls.RichTextEditor? _editor;
        private bool _isClosing = false;

        public FloatingTextEditorWindow(object playerInstance, IVideoItem targetItem, Point initialScreenPos)
        {
            InitializeComponent();

            _playerInstance = playerInstance;
            _targetItem = targetItem;
            _textItem = targetItem as TextItem;
            _voiceItem = targetItem as VoiceItem;

            // 初期位置の計算（カーソルの右下に配置、テキストに被らないようにする）
            double workW = SystemParameters.WorkArea.Right;
            double workH = SystemParameters.WorkArea.Bottom;
            double workLeft = SystemParameters.WorkArea.Left;
            double workTop = SystemParameters.WorkArea.Top;

            double targetLeft = initialScreenPos.X + 16;
            double targetTop = initialScreenPos.Y + 16;

            // 画面右端からはみ出る場合はカーソルの左側に反転
            if (targetLeft + Width > workW - 10)
            {
                targetLeft = Math.Max(workLeft + 10, initialScreenPos.X - Width - 16);
            }

            // 画面下端からはみ出る場合はカーソルの上側に反転
            if (targetTop + Height > workH - 10)
            {
                targetTop = Math.Max(workTop + 10, initialScreenPos.Y - Height - 16);
            }

            Left = Math.Max(workLeft + 10, targetLeft);
            Top = Math.Max(workTop + 10, targetTop);

            // タイトルバーでのドラッグ移動
            TitleBar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    DragMove();
                }
            };

            // 閉じるボタン
            CloseButton.Click += (s, e) => Close();

            // イベント
            Loaded += OnLoaded;
            Closed += OnClosed;
            PreviewKeyDown += OnPreviewKeyDown;

            // YMM4公式のアイテム設定テキスト入力コントロールを生成・バインド
            SetupRichTextEditor();
        }

        private void SetupRichTextEditor()
        {
            try
            {
                // YMM4公式の RichTextEditor をインスタンス化
                _editor = new YukkuriMovieMaker.Controls.RichTextEditor
                {
                    IsShiftEnterPlaceHolderEnabled = true,
                    // YMM4の設定に応じた決定キー（Enter / Shift+Enter）での決定を有効化
                    IsEnterToClearFocusEnabled = true,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };

                // 決定キー（ユーザーの設定でEnter決定またはShift+Enter決定）が押されたら自動で閉じる
                _editor.Submit += (s, e) =>
                {
                    Close();
                };

                // アイテム設定ウィンドウ（RichTextEditorAttribute.SetBindings）と完全に同一のバインディングを適用
                if (_textItem != null)
                {
                    _editor.SetBinding(YukkuriMovieMaker.Controls.RichTextEditor.TextProperty, new Binding("Text")
                    {
                        Source = _textItem,
                        Mode = BindingMode.TwoWay,
                        Delay = 1
                    });
                    _editor.SetBinding(YukkuriMovieMaker.Controls.RichTextEditor.DecorationsProperty, new Binding("Decorations")
                    {
                        Source = _textItem,
                        Mode = BindingMode.TwoWay,
                        Delay = 1
                    });
                    _editor.SetBinding(YukkuriMovieMaker.Controls.RichTextEditor.FontColorProperty, new Binding("FontColor")
                    {
                        Source = _textItem,
                        Mode = BindingMode.OneWay
                    });
                    _editor.SetBinding(YukkuriMovieMaker.Controls.RichTextEditor.TextFontProperty, new Binding("Font")
                    {
                        Source = _textItem,
                        Mode = BindingMode.OneWay
                    });

                    _textItem.PropertyChanged += OnItemPropertyChanged;
                }
                else if (_voiceItem != null)
                {
                    _editor.SetBinding(YukkuriMovieMaker.Controls.RichTextEditor.TextProperty, new Binding("Serif")
                    {
                        Source = _voiceItem,
                        Mode = BindingMode.TwoWay,
                        Delay = 1
                    });
                    _editor.SetBinding(YukkuriMovieMaker.Controls.RichTextEditor.FontColorProperty, new Binding("FontColor")
                    {
                        Source = _voiceItem,
                        Mode = BindingMode.OneWay
                    });
                    _editor.SetBinding(YukkuriMovieMaker.Controls.RichTextEditor.TextFontProperty, new Binding("Font")
                    {
                        Source = _voiceItem,
                        Mode = BindingMode.OneWay
                    });

                    _voiceItem.PropertyChanged += OnItemPropertyChanged;
                }

                // ウィンドウ中央のコンテンツ領域に公式コントロールを配置
                EditorContainer.Content = _editor;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SetupRichTextEditor Error] {ex}");
            }
        }

        private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Text" || e.PropertyName == "Decorations" || e.PropertyName == "Serif")
            {
                // リアルタイムにプレビュー画面を再描画
                CoordinateConverter.NotifyTimelineChanged(_playerInstance);
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // ロード完了時にコントロールへ自動フォーカス
            _editor?.SetFocus();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Escape キーで編集ウィンドウを確定して閉じる
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            if (_isClosing) return;
            _isClosing = true;

            if (_textItem != null)
            {
                _textItem.PropertyChanged -= OnItemPropertyChanged;
            }
            if (_voiceItem != null)
            {
                _voiceItem.PropertyChanged -= OnItemPropertyChanged;
            }

            Commit();
        }

        private void Commit()
        {
            try
            {
                CoordinateConverter.NotifyTimelineChanged(_playerInstance);

                var undoProp = _playerInstance.GetType().GetProperty("UndoRedoManager", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var undo = undoProp?.GetValue(_playerInstance) as UndoRedoManager;
                undo?.Record();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Commit Error] {ex}");
            }
        }
    }
}
