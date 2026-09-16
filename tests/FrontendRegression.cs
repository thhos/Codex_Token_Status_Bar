using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexPetCredits;

public static class FrontendRegression {
    private static int failures;
    private static void Check(string name, Action test) { try { test(); Console.WriteLine("PASS " + name); } catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e.GetBaseException().Message); } }
    private static void Require(bool value, string reason) { if (!value) throw new Exception(reason); }
    [STAThread] public static int Main(string[] args) {
        new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Check("first chart frame stays inside plot", delegate {
            var chart = new CreditChart(); chart.Measure(new Size(320, 126)); chart.Arrange(new Rect(0, 0, 320, 126));
            chart.SetSeries(new List<double?[]> { new double?[] { 10000, 20000, 10000 } });
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen()) typeof(CreditChart).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(chart, new object[] { context });
            Require(visual.Drawing.Bounds.Top >= -2, "curve escapes chart top: " + visual.Drawing.Bounds.Top);
            chart.FinishAnimation();
        });
        Check("long task titles do not expand trend panel or hover text", delegate {
            var serializer = new JavaScriptSerializer();
            var fixture = (Dictionary<string, object>)serializer.DeserializeObject(File.ReadAllText(Path.Combine(args[0], "tests", "view-fixture.json")));
            var settings = (Dictionary<string, object>)fixture["settings"]; settings["density"] = 1;
            string longTitle = String.Concat(Enumerable.Repeat("这是一个包含很多细节的任务标题", 60));
            fixture["subtitle"] = longTitle;
            foreach (var series in (object[])fixture["series"]) ((Dictionary<string, object>)series)["name"] = longTitle;
            var window = new CompanionWindow(args[0], true); window.UpdateView(fixture); window.Show(); window.UpdateLayout();
            try {
                Require(window.ActualHeight <= 440, "trend panel expanded to " + window.ActualHeight);
                foreach (var element in Descendants(window).OfType<FrameworkElement>()) if (element.ToolTip is string) Require(((string)element.ToolTip).Length <= 100, "unbounded hover text");
            } finally { window.Close(); }
        });
        Check("automatic placement avoids pet and native bar at every edge", delegate {
            var work = new Rect(0, 0, 1600, 1000); var size = new Size(352, 360);
            foreach (var point in new[] { new Point(700,500),new Point(4,4),new Point(1480,4),new Point(4,860),new Point(1480,860),new Point(700,4),new Point(700,860) }) {
                var pet = new Rect(point.X,point.Y,112,122);
                var bar = new Rect(Math.Max(0,Math.Min(1270,point.X-109)),Math.Max(0,point.Y-68),330,56);
                var result = new AttachmentPlacement().Place(pet,size,work,new[]{bar},false);
                Require(work.Contains(result),"panel leaves work area");
                Require(!result.IntersectsWith(pet),"panel covers pet");
                Require(!result.IntersectsWith(bar),"panel covers native floating bar");
                if(point.Y==500)Require(result.Bottom<bar.Top,"normal position must prefer above, not a fixed left/right side");
            }
            var controller = new AttachmentPlacement();
            controller.Place(new Rect(700,0,112,122),size,work,new Rect[0],false);
            Require(controller.Place(new Rect(700,600,112,122),size,work,new Rect[0],false).Bottom<600,"returns above when room is available");
        });
        Check("legend stays readable after switching from dark to light", delegate {
            var serializer = new JavaScriptSerializer();
            var fixture = (Dictionary<string, object>)serializer.DeserializeObject(File.ReadAllText(Path.Combine(args[0], "tests", "view-fixture.json")));
            var settings = (Dictionary<string, object>)fixture["settings"]; settings["density"] = 2; settings["theme"] = "dark";
            var window = new CompanionWindow(args[0], true); window.UpdateView(fixture); window.Show(); window.UpdateLayout();
            settings["theme"] = "light"; window.UpdateView(fixture); window.UpdateLayout();
            try {
                var texts = Descendants(window).OfType<TextBlock>().Where(t => t.Text == "Status Bar").ToArray();
                Require(texts.Length > 0,"legend label missing");
                foreach (var text in texts) Require(((SolidColorBrush)text.Foreground).Color.R < 140,"legend retains pale dark-theme text");
            } finally { window.Close(); }
        });
        Check("changing task never interpolates unrelated credit values", delegate {
            var chart = new CreditChart { ContextKey="first" };chart.SetSeries(new List<double?[]>{new double?[]{1000,2000}});chart.FinishAnimation();
            chart.ContextKey="second";chart.SetSeries(new List<double?[]>{new double?[]{1,2}});
            var current=(List<double?[]>)typeof(CreditChart).GetMethod("Current",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(chart,null);
            Require(current[0][0]==1 && current[0][1]==2,"new task inherits old task curve");
            chart.FinishAnimation();
        });
        Check("child accessibility events never count as hiding the pet window", delegate {
            var native = typeof(CompanionWindow).Assembly.GetType("CodexPetCredits.Native");
            var lifecycle = native.GetMethod("IsWindowLifecycleEvent");
            Require(!(bool)lifecycle.Invoke(null,new object[]{(uint)0x8003,-4,0}),"child hide event hides the panel");
            Require(!(bool)lifecycle.Invoke(null,new object[]{(uint)0x8001,0,2}),"child destroy event hides the panel");
            Require((bool)lifecycle.Invoke(null,new object[]{(uint)0x8003,0,0}),"real window hide was ignored");
        });
        Check("direction changes move continuously around the pet", delegate {
            var pet=new Rect(700,390,100,120);var from=new Rect(600,140,320,200);var to=new Rect(600,550,320,200);var work=new Rect(0,0,1600,1000);
            var transition=new AttachmentTransition(from,to,work,new[]{pet});
            Require(!transition.FadeThrough,"a clear route should animate movement");
            var previous=transition.Sample(0);Require(previous==from.TopLeft,"wrong transition start");
            for(int i=1;i<=60;i++) {var point=transition.Sample(i/60.0);Require((point-previous).Length<40,"animation teleports between frames");Require(!new Rect(point,to.Size).IntersectsWith(pet),"animation crosses the pet");previous=point;}
            Require(transition.Sample(1)==to.TopLeft,"transition misses destination");
            var blocked=new AttachmentTransition(new Rect(0,0,320,200),new Rect(0,700,320,200),new Rect(0,0,330,1000),new[]{new Rect(0,400,330,100)});
            Require(blocked.FadeThrough && blocked.Visibility(.5)==0,"no-space transition must relocate while fully faded");
        });
        Check("smoothing reduces spikes without filling missing data or changing raw samples", delegate {
            var raw=new double?[]{0,0,30,0,0,null,80,80,80};var result=CurveSmoothing.Apply(raw);
            Require(result[2]<30 && result[2]>0,"spike remains jagged");Require(!result[5].HasValue,"missing data was invented");
            Require(result[6]==80 && raw[2]==30,"filter crossed gap or changed raw credits");
            var chart=new CreditChart{Smooth=true};chart.SetSeries(new List<double?[]>{raw});chart.FinishAnimation();
            var stored=(List<double?[]>)typeof(CreditChart).GetField("raw",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(chart);
            Require(stored[0][2]==30,"hover data lost the original spike");
        });
        Check("theme colors and expanded sections work without unbounded layout", delegate {
            var fixture=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(args[0],"tests","view-fixture.json")));
            var settings=(Dictionary<string,object>)fixture["settings"];settings["density"]=2;
            var window=new CompanionWindow(args[0],true);window.Show();var colors=new HashSet<Color>();
            try{foreach(string accent in Palette.Keys){settings["accent"]=accent;window.UpdateView(fixture);window.UpdateLayout();var value=(TextBlock)typeof(CompanionWindow).GetField("amount",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);colors.Add(((SolidColorBrush)value.Foreground).Color);}
                Require(colors.Count==5,"theme selector does not change displayed accent");
                foreach(var expander in Descendants(window).OfType<Expander>().ToArray())expander.IsExpanded=true;window.UpdateLayout();Require(window.ActualHeight<850,"details overflow the panel");
            }finally{window.Close();}
        });
        Check("chart bridges bounded gaps with dashes without inventing hover values", delegate {
            var start=new DateTime(2026,9,1);var chart=new CreditChart{Start=start,End=start.AddDays(30),ObservedAt=start.AddDays(30),Smooth=true};
            var values=new List<double?[]>{new double?[]{null,10,null,null,20,null},new double?[]{null,null,4,null,null,null}};
            chart.Measure(new Size(320,142));chart.Arrange(new Rect(0,0,320,142));chart.SetSeries(values);chart.FinishAnimation();
            var visual=new DrawingVisual();using(var dc=visual.RenderOpen())typeof(CreditChart).GetMethod("OnRender",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(chart,new object[]{dc});
            var bridges=Drawings(visual.Drawing).OfType<GeometryDrawing>().Where(d=>d.Pen!=null && d.Pen.DashStyle.Dashes.Count>0).ToArray();
            Require(bridges.Length==1,"gaps were left blank, extrapolated at edges, or joined across series");
            Require(bridges[0].Bounds.Left>70 && bridges[0].Bounds.Right<250,"bridge extends past adjacent valid samples");
            Require(chart.DescribeBucket(2).Contains("数据不完整") && chart.DescribeBucket(2).Contains("虚线仅连接趋势"),"missing interval tooltip hides interpolation semantics");
            var stored=(List<double?[]>)typeof(CreditChart).GetField("raw",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(chart);
            Require(!stored[0][2].HasValue && stored[0][1]==10 && stored[0][4]==20,"bridge changed recorded consumption");
            chart.SetSeries(new List<double?[]>{new double?[]{null,null,null}});chart.FinishAnimation();
            using(var dc=visual.RenderOpen())typeof(CreditChart).GetMethod("OnRender",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(chart,new object[]{dc});
            Require(!Drawings(visual.Drawing).OfType<GeometryDrawing>().Any(d=>d.Pen!=null && d.Pen.DashStyle.Dashes.Count>0),"all-missing data creates a curve");
        });
        Check("longer chart windows retain more variation under smoothing", delegate {
            var start=new DateTime(2026,9,1);var raw=new double?[]{0,0,100,0,0,null,80,80};double previousPeak=0;
            foreach(int hours in new[]{1,6,24,168,720}){
                var chart=new CreditChart{Start=start,End=start.AddHours(hours),Smooth=true};chart.SetSeries(new List<double?[]>{raw});chart.FinishAnimation();
                var points=((List<double?[]>)typeof(CreditChart).GetField("target",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(chart))[0];
                Require(points[2]>previousPeak && points[2]<100,"smoothing fails to weaken for a longer window");Require(!points[5].HasValue && points[6]==80,"adaptive smoothing crosses a gap");previousPeak=points[2].Value;
                chart.Smooth=false;chart.SetSeries(new List<double?[]>{raw});chart.FinishAnimation();
                var unchanged=((List<double?[]>)typeof(CreditChart).GetField("target",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(chart))[0];Require(unchanged.SequenceEqual(raw),"raw mode still smooths data");
            }
        });
        Check("chart hover opens immediately and shows rates for completed and partial intervals", delegate {
            var start=new DateTime(2026,9,16,9,0,0);var chart=new CreditChart{Start=start,End=start.AddMinutes(2),ObservedAt=start.AddSeconds(90)};
            chart.SetSeries(new List<double?[]>{new double?[]{12,24}});
            Require(chart.DescribeBucket(0).Contains("12 cr/min"),"full-interval speed is wrong");
            Require(chart.DescribeBucket(1).Contains("48 cr/min") && chart.DescribeBucket(1).Contains("09:01:30"),"partial interval speed/time is wrong");
            var window=new Window{Content=chart,Width=350,Height=180,ShowActivated=false};window.Show();window.UpdateLayout();
            try{typeof(CreditChart).GetField("hover",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(chart,1);typeof(CreditChart).GetMethod("UpdateTooltip",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(chart,null);Require(((ToolTip)chart.ToolTip).IsOpen,"hover tip did not open");}
            finally{window.Close();}
        });
        Check("summary typography aligns and task/project checkboxes apply a draft", delegate {
            var fixture=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(args[0],"tests","view-fixture.json")));
            var settings=(Dictionary<string,object>)fixture["settings"];settings["density"]=1;
            var window=new CompanionWindow(args[0],true);window.UpdateView(fixture);window.Show();window.UpdateLayout();
            Func<string,object> field=name=>typeof(CompanionWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
            try{var amount=(TextBlock)field("amount");var forecast=(TextBlock)field("forecast");
                Require(amount.FontSize==forecast.FontSize,"summary fonts differ");
                Require(Math.Abs(amount.TranslatePoint(new Point(),window).Y-forecast.TranslatePoint(new Point(),window).Y)<1,"summary baselines differ");
                typeof(CompanionWindow).GetMethod("ShowSelection",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,null);
                var popup=(System.Windows.Controls.Primitives.Popup)field("selectionPopup");popup.Child.UpdateLayout();var boxes=Descendants(popup.Child).OfType<CheckBox>().ToArray();Require(boxes.Length==3,"comparison options missing");
                boxes[0].IsChecked=false;boxes[2].IsChecked=true;
                var apply=Descendants(popup.Child).OfType<Button>().First(b=>Convert.ToString(b.Content)=="应用");apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var saved=(Dictionary<string,object>)field("settings");var ids=(string[])saved["selectedProjects"];Require(ids.SequenceEqual(new[]{"project-b","project-c"}),"checkbox draft did not apply");Require(!popup.IsOpen,"picker did not close after apply");
            }finally{window.Close();}
        });
        Check("summary uses complete two-line meanings and a yellow warning", delegate {
            var fixture=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(args[0],"tests","view-fixture.json")));
            var window=new CompanionWindow(args[0],true);window.UpdateView(fixture);window.Show();window.UpdateLayout();
            Func<string,TextBlock> text=name=>(TextBlock)typeof(CompanionWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
            try{
                Require(text("forecast").Text=="9/19 晚上","date must be on the main forecast line");
                Require(text("forecastLabel").Text=="！预计提前耗尽 · 3 张重置券","coupon count must join the warning caption");
                var warning=((SolidColorBrush)text("forecastLabel").Foreground).Color;Require(warning.R>220 && warning.G>150 && warning.B<130,"warning is not yellow");
                Require(window.Width==368,"companion is too wide");
                Require(((Grid)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(text("forecast").Parent))).Children.Count==2,"summary must contain only two cards");
                fixture["forecastDisplay"]=new Dictionary<string,object>{{"value","12/31 晚上"},{"label","！预计提前耗尽"}};fixture["resetDisplay"]="12/31 中午";window.UpdateView(fixture);window.UpdateLayout();
                foreach(string name in new[]{"forecast","forecastLabel"}){var label=text(name);var measure=new FormattedText(label.Text,System.Globalization.CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,new Typeface(label.FontFamily,label.FontStyle,label.FontWeight,label.FontStretch),label.FontSize,label.Foreground,1);Require(measure.Width<=label.ActualWidth+1,"summary text clips: "+name);}
                fixture["resetCreditCount"]=0;window.UpdateView(fixture);Require(text("forecastLabel").Text=="！预计提前耗尽 · 无重置券","zero coupons are unclear");
                fixture["resetCreditCount"]=null;window.UpdateView(fixture);Require(text("forecastLabel").Text=="！预计提前耗尽 · 券数未知","missing coupons are treated as zero");
                fixture["resetCreditCount"]=0;fixture["warning"]=false;fixture["forecastDisplay"]=new Dictionary<string,object>{{"value","至重置"},{"label","预计不会耗尽"}};window.UpdateView(fixture);
                Require(text("forecastLabel").Text=="预计不会耗尽","safe forecast still displays warning coupons");Require(((SolidColorBrush)text("forecastLabel").Foreground).Color!=warning,"warning color persisted after recovery");
            }finally{window.Close();}
        });
        Check("value updates fade out then in and discard superseded samples", delegate {
            var label=new TextBlock{Text="78%"};var window=new Window{Content=label,Width=150,Height=100,ShowActivated=false};window.Show();window.UpdateLayout();
            try{TextTransition.Set(label,"77%");Pump(55);if(SystemParameters.ClientAreaAnimation)Require(label.Opacity<1 && label.Text=="78%","old value did not fade before replacement");TextTransition.Set(label,"76%");Pump(400);Require(label.Text=="76%" && Math.Abs(label.Opacity-1)<.001,"newest value failed to settle");TextTransition.Set(label,"76%");Require(!label.HasAnimatedProperties,"unchanged value reanimated");}
            finally{window.Close();}
        });
        Check("detail quota refresh preserves rows and updates independent quota changes", delegate {
            var fixture=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(args[0],"tests","view-fixture.json")));
            fixture["otherQuotas"]=new object[]{new Dictionary<string,object>{{"name","其他额度"},{"value","80%"}}};
            var window=new CompanionWindow(args[0],true);window.UpdateView(fixture);
            var rows=(StackPanel)typeof(CompanionWindow).GetField("accountRows",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
            var row=(Grid)rows.Children[0];fixture["otherQuotas"]=new object[]{new Dictionary<string,object>{{"name","其他额度"},{"value","79%"}}};window.UpdateView(fixture);
            Require(Object.ReferenceEquals(row,rows.Children[0]),"quota refresh rebuilt the visual row");Require(row.Children.OfType<TextBlock>().Single().Text=="79%","independent quota update was skipped");window.Close();
        });
        return failures == 0 ? 0 : 1;
    }
    private static void Pump(int milliseconds) { var frame=new System.Windows.Threading.DispatcherFrame();var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(milliseconds)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();System.Windows.Threading.Dispatcher.PushFrame(frame); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var next in Descendants(child)) yield return next; }
    }
    private static IEnumerable<Drawing> Drawings(Drawing drawing) {
        yield return drawing;var group=drawing as DrawingGroup;
        if(group!=null)foreach(var child in group.Children)foreach(var descendant in Drawings(child))yield return descendant;
    }
}
