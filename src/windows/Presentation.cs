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
}
