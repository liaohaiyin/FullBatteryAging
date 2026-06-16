using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using ScottPlot;
using ScottPlot.WPF;

namespace BatteryAging.UI.Helpers
{
    /// <summary>
    /// 鼠标悬停时，在最近的数据点周围画空心圆并显示数值。
    /// 支持单张图多条曲线（含多 Y 轴）。
    /// </summary>
    public sealed class ScatterHighlighter : IDisposable
    {
        private readonly WpfPlot _wpf;
        private readonly List<ScottPlot.Plottables.Scatter> _series;
        private readonly Func<Coordinates, ScottPlot.Plottables.Scatter, string> _format;
        private readonly float _maxPixelDistance;

        private readonly ScottPlot.Plottables.Marker _marker;
        private readonly ScottPlot.Plottables.Text _label;

        // 隐藏时把位置置 NaN，避免参与 AutoScale
        private static readonly Coordinates Hidden = new(double.NaN, double.NaN);

        public ScatterHighlighter(
            WpfPlot wpf,
            IEnumerable<ScottPlot.Plottables.Scatter> series,
            Func<Coordinates, ScottPlot.Plottables.Scatter, string> format = null,
            float maxPixelDistance = 12)
        {
            _wpf = wpf;
            _series = series?.ToList() ?? new();
            _format = format ?? ((c, s) => $"({c.X:F2}, {c.Y:F3})");
            _maxPixelDistance = maxPixelDistance;

            var plot = wpf.Plot;

            _marker = plot.Add.Marker(new Coordinates(0, 0), MarkerShape.OpenCircle, 12, Colors.Yellow);
            _marker.MarkerStyle.LineWidth = 2;
            _marker.Location = Hidden;
            _marker.IsVisible = false;

            _label = plot.Add.Text(" ", 0, 0);
            _label.LabelFontColor = Colors.White;
            _label.LabelBackgroundColor = Colors.Black.WithAlpha(0.65);
            _label.LabelPadding = 4;
            _label.OffsetX = 10;
            _label.OffsetY = -10;
            _label.Location = Hidden;
            _label.IsVisible = false;

            _wpf.MouseMove += OnMouseMove;
            _wpf.MouseLeave += OnMouseLeave;
        }

        /// <summary>曲线被重建（plot.Clear 后重新 Add）时刷新引用</summary>
        public void SetSeries(IEnumerable<ScottPlot.Plottables.Scatter> series)
        {
            _series.Clear();
            if (series != null) _series.AddRange(series);
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(_wpf);
            Pixel mouse = new(
                (float)(pos.X * _wpf.DisplayScale),
                (float)(pos.Y * _wpf.DisplayScale));

            double bestDistSq = double.MaxValue;
            Coordinates best = default;
            ScottPlot.Plottables.Scatter bestScatter = null;

            foreach (var s in _series)
            {
                if (s == null || !s.IsVisible) continue;

                IReadOnlyList<Coordinates> pts;
                try { pts = s.Data.GetScatterPoints(); }
                catch { continue; }

                for (int i = 0; i < pts.Count; i++)
                {
                    // 用该曲线自己的坐标轴换算像素，保证多 Y 轴也准确
                    Pixel pp = s.Axes.GetPixel(pts[i]);
                    double dx = pp.X - mouse.X;
                    double dy = pp.Y - mouse.Y;
                    double d2 = dx * dx + dy * dy;
                    if (d2 < bestDistSq)
                    {
                        bestDistSq = d2;
                        best = pts[i];
                        bestScatter = s;
                    }
                }
            }

            bool hit = bestScatter != null &&
                       bestDistSq <= _maxPixelDistance * _maxPixelDistance;

            if (hit)
            {
                _marker.Axes.XAxis = bestScatter.Axes.XAxis;
                _marker.Axes.YAxis = bestScatter.Axes.YAxis;
                _marker.Location = best;
                _marker.IsVisible = true;

                _label.Axes.XAxis = bestScatter.Axes.XAxis;
                _label.Axes.YAxis = bestScatter.Axes.YAxis;
                _label.Location = best;
                _label.LabelText = _format(best, bestScatter);
                _label.IsVisible = true;

                _wpf.Refresh();
            }
            else if (_marker.IsVisible)
            {
                Hide();
                _wpf.Refresh();
            }
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            if (_marker.IsVisible) { Hide(); _wpf.Refresh(); }
        }

        private void Hide()
        {
            _marker.IsVisible = false;
            _marker.Location = Hidden;
            _label.IsVisible = false;
            _label.Location = Hidden;
        }

        public void Dispose()
        {
            _wpf.MouseMove -= OnMouseMove;
            _wpf.MouseLeave -= OnMouseLeave;
        }
    }
}