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
    // Attach above or below; clamp horizontally and constrain height when vertical space is limited.
    public sealed class AttachmentPlacement {
        private int side = -1;
        public int Side { get { return side; } }
        public Rect Place(Rect pet, Size size, Rect work, IEnumerable<Rect> occupied, bool dragging, double stabilityMargin=64) {
            const double gap=14,edge=8;
            var envelope=pet;
            foreach(var obstacle in occupied??Enumerable.Empty<Rect>())envelope.Union(obstacle);
            double above=Math.Max(1,envelope.Top-gap-work.Top-edge);
            double below=Math.Max(1,work.Bottom-edge-envelope.Bottom-gap);
            bool aboveFits=above>=size.Height,belowFits=below>=size.Height;
            // Keep a usable side during dragging. When neither fits, scroll inside the larger space.
            if(side==0 && aboveFits){}
            else if(side==1 && belowFits && (dragging || above<size.Height+stabilityMargin)){}
            else if(aboveFits)side=0;
            else if(belowFits)side=1;
            else if(side<0 || Math.Abs(above-below)>stabilityMargin)side=above>=below?0:1;
            double height=Math.Min(size.Height,side==0?above:below);
            double width=Math.Min(size.Width,Math.Max(1,work.Width-2*edge));
            double x=Math.Max(work.Left+edge,Math.Min(pet.Left+(pet.Width-width)/2,work.Right-width-edge));
            double y=side==0?envelope.Top-gap-height:envelope.Bottom+gap;
            y=Math.Max(work.Top+edge,Math.Min(y,work.Bottom-height-edge));
            return new Rect(x,y,width,height);
        }
    }

    // Never travel around the pet. The relocation happens during a fully invisible interval.
    public sealed class AttachmentTransition {
        private readonly Point from,to;
        public bool FadeThrough { get { return true; } }
        public AttachmentTransition(Rect from,Rect to){this.from=from.TopLeft;this.to=to.TopLeft;}
        public Point Sample(double progress){return progress<.5?from:to;}
        public double Visibility(double progress){
            progress=Math.Max(0,Math.Min(1,progress));
            double value=progress<.4?1-progress/.4:progress>.6?(progress-.6)/.4:0;
            return value*value*(3-2*value);
        }
    }
}
