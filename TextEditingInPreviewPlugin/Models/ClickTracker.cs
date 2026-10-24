using System;
using System.Drawing;
using System.Windows.Forms;

namespace TextEditingInPreviewPlugin.Models
{
    /// <summary>
    /// WinFormsコントロール上でのダブルクリックを判定するトラッカー
    /// </summary>
    public class ClickTracker
    {
        private long _lastClickTick = 0;
        private Point _lastClickPoint = new Point(int.MinValue, int.MinValue);
        private MouseButtons _lastClickButton = MouseButtons.None;

        public bool IsDoubleClick(MouseEventArgs e)
        {
            if (e.Clicks >= 2)
            {
                return true;
            }

            long currentTick = Environment.TickCount64;
            int doubleClickTime = SystemInformation.DoubleClickTime;
            Size doubleClickSize = SystemInformation.DoubleClickSize;

            bool isTimeValid = (currentTick - _lastClickTick) <= doubleClickTime;
            bool isButtonValid = (_lastClickButton == e.Button);
            bool isPosValid = Math.Abs(e.Location.X - _lastClickPoint.X) <= doubleClickSize.Width &&
                              Math.Abs(e.Location.Y - _lastClickPoint.Y) <= doubleClickSize.Height;

            if (isTimeValid && isButtonValid && isPosValid)
            {
                // ダブルクリック検出後はリセットしてトリプルクリック等を防止
                _lastClickTick = 0;
                _lastClickPoint = new Point(int.MinValue, int.MinValue);
                _lastClickButton = MouseButtons.None;
                return true;
            }

            _lastClickTick = currentTick;
            _lastClickPoint = e.Location;
            _lastClickButton = e.Button;
            return false;
        }

        public void Reset()
        {
            _lastClickTick = 0;
            _lastClickPoint = new Point(int.MinValue, int.MinValue);
            _lastClickButton = MouseButtons.None;
        }
    }
}
