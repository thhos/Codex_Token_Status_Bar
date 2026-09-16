using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace CodexPetCredits {
    // Measure content at its natural height; animate only the space exposed to the window.
    public sealed class AnimatedLayout : Decorator {
        private static readonly DependencyProperty DisplayedHeightProperty=DependencyProperty.Register("DisplayedHeight",typeof(double),typeof(AnimatedLayout),new FrameworkPropertyMetadata(0.0,FrameworkPropertyMetadataOptions.AffectsMeasure));
        private double naturalHeight=Double.NaN;
        private int revision;
        public bool AnimateChanges=true;
        public AnimatedLayout(){ClipToBounds=true;}
        protected override Size MeasureOverride(Size constraint){
            if(Child==null)return new Size();
            Child.Measure(new Size(constraint.Width,Double.PositiveInfinity));var desired=Child.DesiredSize;
            if(Double.IsNaN(naturalHeight) || Math.Abs(desired.Height-naturalHeight)>.5){
                bool animate=!Double.IsNaN(naturalHeight) && IsLoaded && AnimateChanges && SystemParameters.ClientAreaAnimation;
                double from=(double)GetValue(DisplayedHeightProperty);naturalHeight=desired.Height;
                int currentRevision=++revision;BeginAnimation(DisplayedHeightProperty,null);SetValue(DisplayedHeightProperty,naturalHeight);
                if(animate){
                    var animation=new DoubleAnimation(from,naturalHeight,TimeSpan.FromMilliseconds(220)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop};
                    animation.Completed+=delegate{if(revision==currentRevision)BeginAnimation(DisplayedHeightProperty,null);};
                    BeginAnimation(DisplayedHeightProperty,animation);
                }
            }
            return new Size(desired.Width,Math.Max(0,(double)GetValue(DisplayedHeightProperty)));
        }
        protected override Size ArrangeOverride(Size finalSize){
            if(Child!=null)Child.Arrange(new Rect(0,0,finalSize.Width,Math.Max(0,naturalHeight)));
            return finalSize;
        }
    }
}
