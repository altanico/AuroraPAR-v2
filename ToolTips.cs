using System.Windows;
using System.Windows.Controls;

namespace AuroraPAR
{
    internal static class ToolTips
    {
        /// <summary>Delay before a tooltip opens (ms): not at once while a knob is being turned.</summary>
        public const int OpenDelay = 1100;

        /// <summary>
        /// Tooltip kept open while the mouse stays on the element (one minute at most), opening after
        /// <see cref="OpenDelay"/> (also when moving from one control to another).
        /// </summary>
        public static T KeepOpen<T>(T element) where T : DependencyObject
        {
            ToolTipService.SetShowDuration(element, 60000);
            ToolTipService.SetInitialShowDelay(element, OpenDelay);
            ToolTipService.SetBetweenShowDelay(element, 0);
            return element;
        }
    }
}
