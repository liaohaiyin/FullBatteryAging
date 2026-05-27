using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace BatteryAging.UI.AttachedProperties
{
    public static class PlaceholderProperty
    {
        public static readonly DependencyProperty PlaceholderTextProperty =
            DependencyProperty.RegisterAttached("PlaceholderText", typeof(string),
                typeof(PlaceholderProperty), new PropertyMetadata("", OnPlaceholderChanged));

        public static string GetPlaceholderText(DependencyObject obj) =>
            (string)obj.GetValue(PlaceholderTextProperty);

        public static void SetPlaceholderText(DependencyObject obj, string value) =>
            obj.SetValue(PlaceholderTextProperty, value);

        private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBox tb)
            {
                tb.Loaded -= AttachAdorner;
                tb.Loaded += AttachAdorner;
                if (tb.IsLoaded) AttachAdorner(tb, null);
            }
        }

        private static void AttachAdorner(object sender, RoutedEventArgs e)
        {
            var tb = (TextBox)sender;
            var layer = AdornerLayer.GetAdornerLayer(tb);
            if (layer == null) return;
            var existing = layer.GetAdorners(tb);
            if (existing != null)
                foreach (var a in existing)
                    if (a is PlaceholderAdorner) layer.Remove(a);
            layer.Add(new PlaceholderAdorner(tb, GetPlaceholderText(tb)));
        }

        private class PlaceholderAdorner : Adorner
        {
            private readonly TextBlock _text;
            private readonly TextBox _box;

            public PlaceholderAdorner(TextBox box, string placeholder) : base(box)
            {
                _box = box;
                _text = new TextBlock
                {
                    Text = placeholder,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x5C, 0x7A, 0x99)),
                    IsHitTestVisible = false,
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                AddVisualChild(_text);
                _box.TextChanged += (_, _) => InvalidateVisual();
            }
            protected override int VisualChildrenCount => 1;
            protected override Visual GetVisualChild(int index) => _text;
            protected override Size ArrangeOverride(Size finalSize)
            {
                if (string.IsNullOrEmpty(_box.Text))
                    _text.Arrange(new Rect(finalSize));
                else
                    _text.Arrange(new Rect(0, 0, 0, 0));
                return finalSize;
            }
        }
    }
}
