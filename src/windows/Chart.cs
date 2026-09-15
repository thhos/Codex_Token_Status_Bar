using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CodexPetCredits {
    public sealed class CreditChart : FrameworkElement {
        private List<double?[]> previous = new List<double?[]>(), target = new List<double?[]>();
        private List<double?[]> raw = new List<double?[]>();
        private DateTime started = DateTime.MinValue;
        private double oldMax = 1, max = 1;
        private int hover = -1;
        private string appliedKey = "";
        public string ContextKey = "";
        public bool Dark = true;
        public bool Smooth;
        public string Accent = "mint";
        public string[] Names = new string[0];
        public DateTime Start, End;
        public static readonly Color[] Colors = { Color.FromRgb(95,188,164), Color.FromRgb(144,158,236), Color.FromRgb(222,169,106) };
        private Rect Plot { get { return new Rect(2, 22, Math.Max(1, ActualWidth-4), Math.Max(1, ActualHeight-45)); } }
        public CreditChart() {
            Height=142; Focusable=true; ClipToBounds=true; Cursor=Cursors.Cross;
            MouseMove += delegate(object sender, MouseEventArgs e) {
                int count=target.Count==0?0:target[0].Length;
                var p=e.GetPosition(this); if(count==0 || !Plot.Contains(p)){ ClearHover(); return; }
                hover=Math.Max(0,Math.Min(count-1,(int)Math.Floor((p.X-Plot.Left)/Plot.Width*count)));
                FinishAnimation(); UpdateTooltip(); InvalidateVisual();
            };
            MouseLeave += delegate { ClearHover(); };
            KeyDown += delegate(object sender, KeyEventArgs e) {
                int count=target.Count==0?0:target[0].Length;
                if(count>0 && (e.Key==Key.Left || e.Key==Key.Right)){ hover=Math.Max(0,Math.Min(count-1,(hover<0?0:hover)+(e.Key==Key.Left?-1:1))); FinishAnimation(); UpdateTooltip(); InvalidateVisual(); e.Handled=true; }
            };
            Unloaded += delegate { CompositionTarget.Rendering-=Animate; ClearHover(); };
            System.Windows.Automation.AutomationProperties.SetName(this,"Credits 趋势，左右键查看数据");
        }
        private void ClearHover(){ hover=-1; if(ToolTip is ToolTip)((ToolTip)ToolTip).IsOpen=false; ToolTip=null; InvalidateVisual(); }
        public void SetSeries(List<double?[]> values){
            values=values.Select(row=>row.Select(v=>v.HasValue && !Double.IsNaN(v.Value) && !Double.IsInfinity(v.Value) && v.Value>=0?v:null).ToArray()).ToList();
            raw=values; if(Smooth) values=values.Select(CurveSmoothing.Apply).ToList();
            bool contextChanged=appliedKey!=ContextKey;
            bool same=!contextChanged && values.Count==target.Count && values.Select((row,i)=>row.SequenceEqual(target[i])).All(x=>x);
            if(same)return;
            var current=Current(); double currentMax=CurrentMax();
            double peak=values.SelectMany(x=>x).Where(x=>x.HasValue).Select(x=>x.Value).DefaultIfEmpty(0).Max();
            double exponent=peak<=0?1:Math.Pow(10,Math.Floor(Math.Log10(peak))), normalized=peak/exponent;
            double nextMax=peak<=0?1:(normalized<=1?1:normalized<=2?2:normalized<=5?5:10)*exponent;
            bool replace=contextChanged || target.Count==0 || target.Count!=values.Count || target.Select((row,i)=>row.Length!=values[i].Length).Any(x=>x);
            target=values; appliedKey=ContextKey; max=nextMax;
            // New contexts have no valid interpolation source. Initialize values and scale together.
            previous=replace?target:current; oldMax=replace?max:currentMax; started=replace?DateTime.MinValue:DateTime.UtcNow;
            CompositionTarget.Rendering-=Animate;
            if(!replace && hover<0)CompositionTarget.Rendering+=Animate; else FinishAnimation();
            if(contextChanged)ClearHover(); else if(hover>=0)UpdateTooltip();
            InvalidateVisual();
        }
        private double Progress { get { return !SystemParameters.ClientAreaAnimation?1:Math.Min(1,Math.Max(0,(DateTime.UtcNow-started).TotalMilliseconds/180)); } }
        private double Ease { get { return 1-Math.Pow(1-Progress,3); } }
        private double CurrentMax(){ return Math.Max(.000001,oldMax+(max-oldMax)*Ease); }
        private List<double?[]> Current(){
            double t=Ease; var values=new List<double?[]>();
            for(int i=0;i<target.Count;i++){ var row=new double?[target[i].Length]; for(int j=0;j<row.Length;j++){
                if(!target[i][j].HasValue)row[j]=null;
                else { double from=i<previous.Count && j<previous[i].Length && previous[i][j].HasValue?previous[i][j].Value:target[i][j].Value; row[j]=from+(target[i][j].Value-from)*t; }
            } values.Add(row); } return values;
        }
        private void Animate(object sender,EventArgs e){ InvalidateVisual(); if(Progress>=1)CompositionTarget.Rendering-=Animate; }
        public void FinishAnimation(){ started=DateTime.MinValue; CompositionTarget.Rendering-=Animate; InvalidateVisual(); }
        private void UpdateTooltip(){
            if(hover<0 || target.Count==0)return;
            int count=target[0].Length; if(count==0)return;
            var lines=new List<string>{Start.AddTicks((End-Start).Ticks*hover/count).ToString("MM/dd HH:mm")+" · 区间原值"};
            for(int i=0;i<Math.Min(3,raw.Count);i++)if(hover<raw[i].Length)lines.Add(UiText.Short(i<Names.Length?Names[i]:"用量",16)+"  "+(raw[i][hover].HasValue?raw[i][hover].Value.ToString("0.##")+" cr":"缺少数据"));
            var tooltip=ToolTip as ToolTip ?? new ToolTip { MaxWidth=240, PlacementTarget=this };
            tooltip.Content=new TextBlock { Text=String.Join("\n",lines),TextWrapping=TextWrapping.Wrap,MaxWidth=220,FontSize=11 };
            ToolTip=tooltip;
            if(IsKeyboardFocused)tooltip.IsOpen=true;
        }
        protected override void OnRender(DrawingContext dc){
            base.OnRender(dc); var plot=Plot; double scale=CurrentMax(); var data=Current();
            var muted=new SolidColorBrush(Dark?Color.FromRgb(155,164,173):Color.FromRgb(89,100,109));
            dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,ActualWidth,ActualHeight));
            var grid=new Pen(new SolidColorBrush(Dark?Color.FromRgb(49,55,62):Color.FromRgb(222,226,230)),1);
            for(int i=0;i<3;i++)dc.DrawLine(grid,new Point(plot.Left,plot.Top+i*plot.Height/2),new Point(plot.Right,plot.Top+i*plot.Height/2));
            Text(dc,scale.ToString("0.##")+" cr",plot.Left,0,muted,10);
            Text(dc,Start.ToString((End-Start).TotalDays>=2?"MM/dd":"HH:mm"),plot.Left,plot.Bottom+7,muted,10);
            Text(dc,End.ToString((End-Start).TotalDays>=2?"MM/dd":"HH:mm"),Math.Max(plot.Left,plot.Right-32),plot.Bottom+7,muted,10);
            // Clip every animated frame, including gaps and pointer markers, to the plot.
            dc.PushClip(new RectangleGeometry(plot)); bool any=false;
            for(int i=0;i<data.Count;i++){
                var segment=new List<Point>();
                for(int j=0;j<data[i].Length;j++){
                    if(!data[i][j].HasValue){ DrawCurve(dc,segment,Palette.Series(Accent,Dark,i)); segment.Clear(); continue; }
                    any=true; segment.Add(new Point(plot.Left+plot.Width*(j+.5)/data[i].Length,plot.Bottom-Math.Min(1,data[i][j].Value/scale)*plot.Height));
                } DrawCurve(dc,segment,Palette.Series(Accent,Dark,i));
            }
            if(hover>=0 && data.Count>0 && data[0].Length>0){ double x=plot.Left+plot.Width*(hover+.5)/data[0].Length; dc.DrawLine(new Pen(muted,1),new Point(x,plot.Top),new Point(x,plot.Bottom)); }
            dc.Pop();
            if(!any)Text(dc,"此范围暂无记录",Math.Max(2,plot.Width/2-42),plot.Top+plot.Height/2-6,muted,11);
        }
        private static void DrawCurve(DrawingContext dc,List<Point> p,Color color){
            if(p.Count==0)return; var brush=new SolidColorBrush(color);
            if(p.Count==1){ dc.DrawEllipse(brush,null,p[0],2,2); return; }
            var slope=new double[p.Count-1]; var tangent=new double[p.Count];
            for(int i=0;i<slope.Length;i++)slope[i]=(p[i+1].Y-p[i].Y)/(p[i+1].X-p[i].X);
            tangent[0]=slope[0]; tangent[p.Count-1]=slope[slope.Length-1];
            for(int i=1;i<p.Count-1;i++)tangent[i]=slope[i-1]*slope[i]<=0?0:2/(1/slope[i-1]+1/slope[i]);
            var geometry=new StreamGeometry(); using(var context=geometry.Open()){
                context.BeginFigure(p[0],false,false);
                for(int i=0;i<p.Count-1;i++){ double dx=(p[i+1].X-p[i].X)/3; context.BezierTo(new Point(p[i].X+dx,p[i].Y+dx*tangent[i]),new Point(p[i+1].X-dx,p[i+1].Y-dx*tangent[i+1]),p[i+1],true,false); }
            } geometry.Freeze(); dc.DrawGeometry(null,new Pen(brush,1.8),geometry);
        }
        private void Text(DrawingContext dc,string text,double x,double y,Brush brush,double size){
            dc.DrawText(new FormattedText(text,CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(x,y));
        }
    }
}
