using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace CodexPetCredits {
    public static class TextTransition {
        private sealed class Pending { public string Text; public int Revision; }
        private static readonly DependencyProperty PendingProperty = DependencyProperty.RegisterAttached("Pending", typeof(Pending), typeof(TextTransition));
        // Keep only the newest update if another sample arrives during a fade.
        public static void Set(TextBlock element, string text, bool animate = true) {
            var pending = (Pending)element.GetValue(PendingProperty);
            if (pending != null && pending.Text == text) return;
            if (pending == null) { pending = new Pending(); element.SetValue(PendingProperty,pending); }
            pending.Text = text; int revision = ++pending.Revision;
            if (!animate || !element.IsLoaded || !SystemParameters.ClientAreaAnimation || element.Text == text) {
                element.BeginAnimation(UIElement.OpacityProperty,null); element.Opacity=1; element.Text=text; return;
            }
            var fadeOut = new DoubleAnimation(element.Opacity,0,TimeSpan.FromMilliseconds(110)) { EasingFunction=new QuadraticEase{EasingMode=EasingMode.EaseIn} };
            fadeOut.Completed += delegate {
                if(pending.Revision!=revision)return;
                element.Text=text;
                var fadeIn = new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(170)){EasingFunction=new QuadraticEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop};
                // Release the finished clock without interrupting a newer sample.
                fadeIn.Completed += delegate {
                    if(pending.Revision!=revision)return;
                    element.BeginAnimation(UIElement.OpacityProperty,null);element.Opacity=1;
                };
                element.BeginAnimation(UIElement.OpacityProperty,fadeIn);
            };
            element.BeginAnimation(UIElement.OpacityProperty,fadeOut);
        }
        public static void Finish(TextBlock element) {
            var pending=(Pending)element.GetValue(PendingProperty);
            if(pending!=null){pending.Revision++;element.Text=pending.Text;}
            element.BeginAnimation(UIElement.OpacityProperty,null);element.Opacity=1;
        }
    }
}
