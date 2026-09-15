using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CodexPetCredits {
    public sealed class CreditChart : FrameworkElement {
        private List<double?[]> previous = new List<double?[]>(), target = new List<double?[]>();
        private DateTime started = DateTime.MinValue;
        private double oldMax = 1, max = 1;
        private int hover = -1;
        public bool Dark = true;
        public string[] Names = new string[0];
        public DateTime Start, End;
        private static readonly Color[] Colors = { Color.FromRgb(77, 212, 176), Color.FromRgb(137, 155, 255), Color.FromRgb(245, 181, 99) };
        public CreditChart() { Height = 126; Focusable = true; Cursor = Cursors.Cross;
            MouseMove += delegate(object sender, MouseEventArgs e) { hover = Math.Max(0, Math.Min(47, (int)((e.GetPosition(this).X - 4) / Math.Max(1, ActualWidth - 8) * 48))); InvalidateVisual(); };
            MouseLeave += delegate { hover = -1; InvalidateVisual(); };
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Left || e.Key == Key.Right) { hover = Math.Max(0, Math.Min(47, (hover < 0 ? 0 : hover) + (e.Key == Key.Left ? -1 : 1))); InvalidateVisual(); e.Handled = true; } };
            System.Windows.Automation.AutomationProperties.SetName(this, "估算 credits 趋势，左右方向键查看数据");
        }
        public void SetSeries(List<double?[]> values) {
            bool same = values.Count == target.Count;
            if (same) for (int i = 0; i < values.Count; i++) { if (values[i].Length != target[i].Length) { same = false; break; } for (int j = 0; j < values[i].Length; j++) if (values[i][j] != target[i][j]) { same = false; break; } }
            if (same) return;
            previous = Current(); oldMax = CurrentMax(); target = values; max = .01;
            foreach (var series in target) foreach (var value in series) if (value.HasValue) max = Math.Max(max, value.Value);
            max *= 1.15; started = DateTime.UtcNow;
            CompositionTarget.Rendering -= Animate; CompositionTarget.Rendering += Animate; InvalidateVisual();
        }
        private double Progress { get { if (!SystemParameters.ClientAreaAnimation) return 1; return Math.Min(1, Math.Max(0, (DateTime.UtcNow - started).TotalMilliseconds / 260)); } }
        private double Ease { get { double t = Progress; return 1 - Math.Pow(1 - t, 3); } }
        private double CurrentMax() { return oldMax + (max - oldMax) * Ease; }
        private List<double?[]> Current() {
            double t = Ease; var values = new List<double?[]>();
            for (int i = 0; i < target.Count; i++) { var row = new double?[target[i].Length]; for (int j = 0; j < row.Length; j++) {
                if (!target[i][j].HasValue) row[j] = null;
                else { double from = i < previous.Count && j < previous[i].Length && previous[i][j].HasValue ? previous[i][j].Value : target[i][j].Value; row[j] = from + (target[i][j].Value - from) * t; }
            } values.Add(row); } return values;
        }
        private void Animate(object sender, EventArgs e) { InvalidateVisual(); if (Progress >= 1) CompositionTarget.Rendering -= Animate; }
        public void FinishAnimation() { started = DateTime.MinValue; CompositionTarget.Rendering -= Animate; InvalidateVisual(); }
        protected override void OnRender(DrawingContext dc) {
            base.OnRender(dc); double w = ActualWidth, h = ActualHeight - 20, scale = Math.Max(.001, CurrentMax());
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
            var muted = new SolidColorBrush(Dark ? Color.FromRgb(165, 180, 192) : Color.FromRgb(71, 85, 105));
            var grid = new Pen(new SolidColorBrush(Dark ? Color.FromRgb(47, 61, 72) : Color.FromRgb(215, 224, 231)), 1);
            for (int i = 0; i <= 2; i++) dc.DrawLine(grid, new Point(4, 8 + (h - 16) * i / 2), new Point(w - 4, 8 + (h - 16) * i / 2));
            var data = Current(); bool any = false;
            for (int i = 0; i < data.Count; i++) {
                var segment = new List<Point>();
                for (int j = 0; j < data[i].Length; j++) {
                    if (!data[i][j].HasValue) { DrawCurve(dc, segment, Colors[i % Colors.Length]); segment.Clear(); continue; }
                    any = true; segment.Add(new Point(4 + (w - 8) * j / Math.Max(1, data[i].Length - 1), h - 8 - data[i][j].Value / scale * (h - 20)));
                }
                DrawCurve(dc, segment, Colors[i % Colors.Length]);
            }
            Text(dc, (scale / 1.15).ToString("0.##") + " cr / 段", 4, 0, muted, 10);
            Text(dc, Start.ToString("MM/dd HH:mm"), 4, h + 4, muted, 10);
            Text(dc, End.ToString("MM/dd HH:mm"), Math.Max(4, w - 79), h + 4, muted, 10);
            if (!any) Text(dc, "此范围暂无可估算的记录", Math.Max(4, w / 2 - 72), h / 2 - 5, muted, 12);
            if (hover >= 0 && data.Count > 0) {
                double x = 4 + (w - 8) * hover / 47; dc.DrawLine(new Pen(muted, 1), new Point(x, 8), new Point(x, h - 8));
                var lines = new List<string>();
                for (int i = 0; i < target.Count; i++) { int j = Math.Min(hover, target[i].Length - 1); if (j < 0) continue; lines.Add((i < Names.Length ? Names[i] : "曲线") + "  " + (target[i][j].HasValue ? target[i][j].Value.ToString("0.###") + " cr" : "数据不全")); }
                ToolTip = String.Join("\n", lines);
            }
        }
        private static void DrawCurve(DrawingContext dc, List<Point> p, Color color) {
            if (p.Count == 0) return;
            var brush = new SolidColorBrush(color);
            if (p.Count == 1) { dc.DrawEllipse(brush, null, p[0], 2, 2); return; }
            var slope = new double[p.Count - 1]; var tangent = new double[p.Count];
            for (int i = 0; i < slope.Length; i++) slope[i] = (p[i + 1].Y - p[i].Y) / (p[i + 1].X - p[i].X);
            tangent[0] = slope[0]; tangent[p.Count - 1] = slope[slope.Length - 1];
            for (int i = 1; i < p.Count - 1; i++) tangent[i] = slope[i - 1] * slope[i] <= 0 ? 0 : 2 / (1 / slope[i - 1] + 1 / slope[i]);
            var geometry = new StreamGeometry(); using (var context = geometry.Open()) {
                context.BeginFigure(p[0], false, false);
                for (int i = 0; i < p.Count - 1; i++) { double dx = (p[i + 1].X - p[i].X) / 3;
                    context.BezierTo(new Point(p[i].X + dx, p[i].Y + dx * tangent[i]), new Point(p[i + 1].X - dx, p[i + 1].Y - dx * tangent[i + 1]), p[i + 1], true, false); }
            } geometry.Freeze(); dc.DrawGeometry(null, new Pen(brush, 2), geometry);
        }
        private static void Text(DrawingContext dc, string text, double x, double y, Brush brush, double size) {
            dc.DrawText(new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), size, brush, 1.0), new Point(x, y));
        }
    }
}
