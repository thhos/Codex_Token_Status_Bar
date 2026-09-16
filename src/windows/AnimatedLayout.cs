using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace CodexPetCredits {
    // Animate visual layout inside a fixed native surface; never fade the whole panel to resize it.
    public sealed class AnimatedLayout : Decorator {
        private static readonly DependencyProperty DisplayedHeightProperty=DependencyProperty.Register("DisplayedHeight",typeof(double),typeof(AnimatedLayout),new FrameworkPropertyMetadata(0.0,FrameworkPropertyMetadataOptions.AffectsMeasure));
        private double targetHeight=Double.NaN,contentHeight;
        private int revision;
        private bool expanded=true;
        public bool AnimateChanges=true;
        public Func<bool> SuppressAnimation;
        public bool IsAnimating { get; private set; }
        public bool Expanded { get{return expanded;} set{if(expanded!=value){expanded=value;InvalidateMeasure();}} }
        public AnimatedLayout(){ClipToBounds=true;}
        protected override Size MeasureOverride(Size constraint){
            if(Child==null)return new Size();
            Child.Measure(new Size(constraint.Width,Double.PositiveInfinity));var desired=Child.DesiredSize;contentHeight=desired.Height;
            double next=expanded?contentHeight:0;
            if(Double.IsNaN(targetHeight) || Math.Abs(next-targetHeight)>.5){
                bool animate=!Double.IsNaN(targetHeight) && IsLoaded && AnimateChanges && SystemParameters.ClientAreaAnimation && (SuppressAnimation==null || !SuppressAnimation());
                double from=(double)GetValue(DisplayedHeightProperty);targetHeight=next;int currentRevision=++revision;
                BeginAnimation(DisplayedHeightProperty,null);SetValue(DisplayedHeightProperty,targetHeight);IsAnimating=animate;
                if(animate){
                    var animation=new DoubleAnimation(from,targetHeight,TimeSpan.FromMilliseconds(220)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop};
                    animation.Completed+=delegate{if(revision==currentRevision){BeginAnimation(DisplayedHeightProperty,null);IsAnimating=false;}};
                    BeginAnimation(DisplayedHeightProperty,animation);
                }
            }
            return new Size(desired.Width,Math.Max(0,(double)GetValue(DisplayedHeightProperty)));
        }
        protected override Size ArrangeOverride(Size finalSize){
            if(Child!=null)Child.Arrange(new Rect(0,0,finalSize.Width,Math.Max(0,contentHeight)));
            return finalSize;
        }
    }
}
