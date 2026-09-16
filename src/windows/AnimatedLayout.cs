using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CodexPetCredits {
    // Measure content at its natural height; animate only the space exposed to the window.
    public sealed class AnimatedLayout : Decorator {
        private static readonly DependencyProperty DisplayedHeightProperty=DependencyProperty.Register("DisplayedHeight",typeof(double),typeof(AnimatedLayout),new FrameworkPropertyMetadata(0.0,FrameworkPropertyMetadataOptions.AffectsMeasure));
        private double naturalHeight=Double.NaN;
        private int revision;
        private bool fadingOut;
        public bool AnimateChanges=true;
        public FrameworkElement ResizeSurface;
        public AnimatedLayout(){ClipToBounds=true;}
        protected override Size MeasureOverride(Size constraint){
            if(Child==null)return new Size();
            Child.Measure(new Size(constraint.Width,Double.PositiveInfinity));var desired=Child.DesiredSize;
            if(Double.IsNaN(naturalHeight) || Math.Abs(desired.Height-naturalHeight)>.5){
                bool animate=!Double.IsNaN(naturalHeight) && IsLoaded && AnimateChanges && SystemParameters.ClientAreaAnimation;
                double from=(double)GetValue(DisplayedHeightProperty);naturalHeight=desired.Height;
                // A layered HWND can flash when repeatedly shrunk while visible. Freeze its size,
                // fade its surface out, then commit the newest height once before revealing it.
                if(animate && ResizeSurface!=null && (fadingOut || naturalHeight<from || ResizeSurface.Opacity<.999)){
                    if(!fadingOut)ShrinkAfterFade(from);
                    return new Size(desired.Width,Math.Max(0,(double)GetValue(DisplayedHeightProperty)));
                }
                fadingOut=false;
                if(ResizeSurface!=null){ResizeSurface.BeginAnimation(UIElement.OpacityProperty,null);ResizeSurface.Opacity=1;}
                int currentRevision=++revision;BeginAnimation(DisplayedHeightProperty,null);SetValue(DisplayedHeightProperty,naturalHeight);
                if(animate){
                    var animation=new DoubleAnimation(from,naturalHeight,TimeSpan.FromMilliseconds(220)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop};
                    animation.Completed+=delegate{if(revision==currentRevision)BeginAnimation(DisplayedHeightProperty,null);};
                    BeginAnimation(DisplayedHeightProperty,animation);
                }
            }
            return new Size(desired.Width,Math.Max(0,(double)GetValue(DisplayedHeightProperty)));
        }
        private void ShrinkAfterFade(double from){
            int currentRevision=++revision;fadingOut=true;
            BeginAnimation(DisplayedHeightProperty,null);SetValue(DisplayedHeightProperty,from);
            var fadeOut=new DoubleAnimation(ResizeSurface.Opacity,0,TimeSpan.FromMilliseconds(90)){EasingFunction=new QuadraticEase{EasingMode=EasingMode.EaseIn}};
            fadeOut.Completed+=delegate{
                if(currentRevision!=revision)return;
                fadingOut=false;ResizeSurface.BeginAnimation(UIElement.OpacityProperty,null);ResizeSurface.Opacity=0;
                SetValue(DisplayedHeightProperty,naturalHeight);InvalidateMeasure();
                // Wait for layout and follow positioning to settle before painting the smaller surface.
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(delegate{
                    if(currentRevision!=revision)return;
                    var fadeIn=new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(130)){EasingFunction=new QuadraticEase{EasingMode=EasingMode.EaseOut}};
                    fadeIn.Completed+=delegate{if(currentRevision==revision){ResizeSurface.BeginAnimation(UIElement.OpacityProperty,null);ResizeSurface.Opacity=1;}};
                    ResizeSurface.BeginAnimation(UIElement.OpacityProperty,fadeIn);
                }));
            };
            ResizeSurface.BeginAnimation(UIElement.OpacityProperty,fadeOut);
        }
        protected override Size ArrangeOverride(Size finalSize){
            if(Child!=null)Child.Arrange(new Rect(0,0,finalSize.Width,Math.Max(0,naturalHeight)));
            return finalSize;
        }
    }
}
