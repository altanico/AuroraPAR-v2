using System.Windows;
using System.Windows.Controls;

namespace AuroraPAR
{
    internal static class ToolTips
    {
        /// <summary>
        /// Tooltip kept open while the mouse stays on the element (one minute at most).
        /// </summary>
        public static T KeepOpen<T>(T element) where T : DependencyObject
        {
            ToolTipService.SetShowDuration(element, 60000);
            ToolTipService.SetBetweenShowDelay(element, 0);
            return element;
        }
    }
}
