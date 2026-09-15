using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace CodexPetCredits {
    public static class UiText {
        public static string Short(string text, int length = 28) {
            text = String.Join(" ", (text ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            var units = StringInfo.ParseCombiningCharacters(text);
            return units.Length <= length ? text : text.Substring(0, units[Math.Max(1, length - 1)]) + "…";
        }
    }
    // Prefer above/below the pet. Use another direction only when the work area requires it.
    public sealed class AttachmentPlacement {
        private int side = -1;
        public int Side { get { return side; } }
        public Rect Place(Rect pet, Size size, Rect work, IEnumerable<Rect> occupied, bool dragging) {
            const double gap = 14;
            var avoid = new List<Rect>(occupied ?? Enumerable.Empty<Rect>()) { pet };
            var envelope = pet;
            foreach (var obstacle in avoid) envelope.Union(obstacle);
            var candidates = new[] {
                new Rect(pet.Left+(pet.Width-size.Width)/2, envelope.Top-size.Height-gap,size.Width,size.Height),
                new Rect(pet.Left+(pet.Width-size.Width)/2, envelope.Bottom+gap,size.Width,size.Height),
                new Rect(envelope.Right+gap, pet.Top+(pet.Height-size.Height)/2,size.Width,size.Height),
                new Rect(envelope.Left-gap-size.Width, pet.Top+(pet.Height-size.Height)/2,size.Width,size.Height)
            };
            var bounded = candidates.Select(r => new Rect(Math.Max(work.Left+8,Math.Min(r.X,work.Right-size.Width-8)),Math.Max(work.Top+8,Math.Min(r.Y,work.Bottom-size.Height-8)),size.Width,size.Height)).ToArray();
            Func<Rect,double> overlap = r => avoid.Sum(a => { var padded=a; padded.Inflate(6,6); var intersection=Rect.Intersect(r,padded); return intersection.IsEmpty?0:intersection.Width*intersection.Height; });
            bool aboveComfortable = candidates[0].Top >= work.Top + 40 && overlap(bounded[0]) == 0;
            if(side>=0 && overlap(bounded[side])==0 && (side==0 || dragging || !aboveComfortable))return bounded[side];
            side=Enumerable.Range(0,4).OrderBy(i=>overlap(bounded[i])*100+Math.Abs(bounded[i].X-candidates[i].X)+Math.Abs(bounded[i].Y-candidates[i].Y)+(i<2?i:200+i)).First();
            return bounded[side];
        }
    }

    // Route the panel's top-left point around obstacles expanded by the panel's footprint.
    public sealed class AttachmentTransition {
        private readonly List<Point> path;
        private readonly double length;
        public bool FadeThrough { get; private set; }
        public AttachmentTransition(Rect from, Rect to, Rect work, IEnumerable<Rect> obstacles) {
            var bounds = new Rect(work.Left + 2, work.Top + 2, Math.Max(1, work.Width - to.Width - 4), Math.Max(1, work.Height - to.Height - 4));
            var blocked = obstacles.Select(r => new Rect(r.Left - to.Width - 7, r.Top - to.Height - 7, r.Width + to.Width + 14, r.Height + to.Height + 14)).ToArray();
            var nodes = new List<Point> { from.TopLeft, to.TopLeft };
            foreach (var r in blocked) foreach (var point in new[] { r.TopLeft, r.TopRight, r.BottomLeft, r.BottomRight }) {
                if (bounds.Contains(point) && !blocked.Any(b => Inside(b, point))) nodes.Add(point);
            }
            var distance = Enumerable.Repeat(Double.PositiveInfinity, nodes.Count).ToArray();
            var parent = Enumerable.Repeat(-1, nodes.Count).ToArray(); var visited = new bool[nodes.Count]; distance[0] = 0;
            for (int step = 0; step < nodes.Count; step++) {
                int current = -1;
                for (int i = 0; i < nodes.Count; i++) if (!visited[i] && (current < 0 || distance[i] < distance[current])) current = i;
                if (current < 0 || Double.IsInfinity(distance[current])) break;
                visited[current] = true; if (current == 1) break;
                for (int next = 0; next < nodes.Count; next++) if (!visited[next] && !blocked.Any(r => Crosses(nodes[current], nodes[next], r))) {
                    double candidate = distance[current] + (nodes[next] - nodes[current]).Length;
                    if (candidate < distance[next]) { distance[next] = candidate; parent[next] = current; }
                }
            }
            path = new List<Point>(); FadeThrough = Double.IsInfinity(distance[1]);
            if (FadeThrough) { path.Add(from.TopLeft); path.Add(to.TopLeft); }
            else { for (int at = 1; at >= 0; at = parent[at]) path.Insert(0, nodes[at]); }
            for (int i = 1; i < path.Count; i++) length += (path[i] - path[i-1]).Length;
        }
        public Point Sample(double progress) {
            progress = Math.Max(0, Math.Min(1, progress));
            if (FadeThrough) return progress < .5 ? path[0] : path[path.Count-1];
            double remaining = (progress * progress * (3 - 2 * progress)) * length;
            for (int i = 1; i < path.Count; i++) { double segment = (path[i] - path[i-1]).Length; if (remaining <= segment) return path[i-1] + (path[i] - path[i-1]) * (segment == 0 ? 1 : remaining / segment); remaining -= segment; }
            return path[path.Count-1];
        }
        public double Visibility(double progress) { return FadeThrough ? Math.Abs(Math.Max(0,Math.Min(1,progress)) * 2 - 1) : 1; }
        private static bool Inside(Rect r, Point p) { return p.X > r.Left+.01 && p.X < r.Right-.01 && p.Y > r.Top+.01 && p.Y < r.Bottom-.01; }
        private static bool Crosses(Point a, Point b, Rect r) {
            // Open interiors allow a path to touch an expanded corner without crossing it.
            r.Inflate(-.01,-.01); double low=0,high=1; var delta=b-a;
            foreach (int axis in new[]{0,1}) { double origin=axis==0?a.X:a.Y, speed=axis==0?delta.X:delta.Y, min=axis==0?r.Left:r.Top, max=axis==0?r.Right:r.Bottom;
                if(Math.Abs(speed)<.000001){if(origin<min || origin>max)return false;}
                else{double t0=(min-origin)/speed,t1=(max-origin)/speed;if(t0>t1){double swap=t0;t0=t1;t1=swap;}low=Math.Max(low,t0);high=Math.Min(high,t1);if(low>high)return false;}
            } return true;
        }
    }
}
