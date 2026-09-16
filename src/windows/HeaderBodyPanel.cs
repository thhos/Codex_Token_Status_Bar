using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CodexPetCredits {
    // Anchor the body independently of the animated header's rounded height.
    // Above the pet, the panel bottom and the body bottom are the same fixed edge.
    public sealed class HeaderBodyPanel : Panel {
        private readonly FrameworkElement header,body;
        public HeaderBodyPanel(FrameworkElement header,FrameworkElement body){
            this.header=header;this.body=body;Children.Add(header);Children.Add(body);
        }
        protected override Size MeasureOverride(Size available){
            var constraint=new Size(available.Width,Double.PositiveInfinity);
            header.Measure(constraint);body.Measure(constraint);
            return new Size(Math.Max(header.DesiredSize.Width,body.DesiredSize.Width),header.DesiredSize.Height+body.DesiredSize.Height);
        }
        protected override Size ArrangeOverride(Size size){
            double dpi=VisualTreeHelper.GetDpi(this).DpiScaleY;
            double bodyHeight=Math.Round(body.DesiredSize.Height*dpi)/dpi;
            double bodyTop=size.Height-bodyHeight;
            body.Arrange(new Rect(0,bodyTop,size.Width,bodyHeight));
            header.Arrange(new Rect(0,bodyTop-header.DesiredSize.Height,size.Width,header.DesiredSize.Height));
            return size;
        }
    }
}
