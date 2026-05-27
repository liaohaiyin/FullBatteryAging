using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BatteryAging.UI.Controls
{
    /// <summary>
    /// 简易图标控件，用 Path Geometry 渲染单色矢量图标。
    /// 用法: &lt;ctrl:VsIcon Kind="{StaticResource Icon.Play}" Width="16" Height="16" Foreground="..."/&gt;
    /// </summary>
    public class VsIcon : Control
    {
        static VsIcon()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(VsIcon),
                new FrameworkPropertyMetadata(typeof(VsIcon)));
        }

        public static readonly DependencyProperty KindProperty =
            DependencyProperty.Register(
                nameof(Kind),
                typeof(Geometry),
                typeof(VsIcon),
                new PropertyMetadata(null));

        /// <summary>
        /// 图标几何 (24x24 viewBox)
        /// </summary>
        public Geometry Kind
        {
            get => (Geometry)GetValue(KindProperty);
            set => SetValue(KindProperty, value);
        }
    }
}
