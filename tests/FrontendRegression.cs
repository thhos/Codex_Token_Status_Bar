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
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct ScreenPoint { public int X,Y; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(ScreenPoint point);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr handle,uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct ScreenRect { public int Left,Top,Right,Bottom; }
    private delegate bool MonitorCallback(IntPtr monitor,IntPtr dc,ref ScreenRect rect,IntPtr data);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorCallback callback,IntPtr data);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle,IntPtr after,int x,int y,int width,int height,uint flags);
    private static int failures;
    private static void Check(string name, Action test) { try { test(); Console.WriteLine("PASS " + name); } catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e.GetBaseException().Message); } }
    private static void Require(bool value, string reason) { if (!value) throw new Exception(reason); }
    [STAThread] public static int Main(string[] args) {
        // Match the production process: virtualized 96-DPI tests mask fractional-pixel jitter.
        SetProcessDpiAwarenessContext(new IntPtr(-4));
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
                Require(window.SurfaceHeight <= 440, "trend panel expanded to " + window.SurfaceHeight);
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
                foreach(var expander in Descendants(window).OfType<Expander>().ToArray())expander.IsExpanded=true;window.UpdateLayout();Require(window.SurfaceHeight<850,"details overflow the panel");
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
        Check("chart selection shows inline values and time without a mouse popup", delegate {
            var start=new DateTime(2026,9,16,9,0,0);var chart=new CreditChart{Start=start,End=start.AddMinutes(2),ObservedAt=start.AddSeconds(90)};
            chart.SetSeries(new List<double?[]>{new double?[]{12,24}});
            Require(chart.DescribeBucket(0).Contains("12 cr/min"),"full-interval speed is wrong");
            Require(chart.DescribeBucket(1).Contains("48 cr/min") && chart.DescribeBucket(1).Contains("09:01:30"),"partial interval speed/time is wrong");
            var window=new Window{Content=chart,Width=350,Height=180,ShowActivated=false};window.Show();window.UpdateLayout();
            try{
                chart.SelectBucket(1);Require(chart.ToolTip==null,"chart still creates a mouse popup");Require(chart.SelectedValue=="24 cr · 48 cr/min" && chart.SelectedTime=="09/16 09:01:00–09:01:30","inline partial interval is wrong");
                chart.SetSeries(new List<double?[]>{new double?[]{12,24},new double?[]{3,6}});chart.SelectBucket(1);Require(chart.SelectedValue=="合计 30 cr · 60 cr/min","multi-series readout is ambiguous");
                chart.SetSeries(new List<double?[]>{new double?[]{12,null}});chart.SelectBucket(1);Require(chart.SelectedValue=="— cr · — cr/min","missing interval invented a value");
                typeof(CreditChart).GetMethod("ClearHover",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(chart,null);Require(chart.SelectedValue=="" && chart.SelectedTime=="","leaving chart retains selection");
            }
            finally{window.Close();}
        });
        Check("layout expansion and interrupted collapse animate to the newest height", delegate {
            var body=new Border{Height=80,Background=Brushes.Gray};var host=new AnimatedLayout{Child=body};var window=new Window{Content=host,Width=220,SizeToContent=SizeToContent.Height,ShowActivated=false};window.Show();window.UpdateLayout();Pump(30);Require(host.IsLoaded,"animation host did not load");
            try{
                body.Height=320;window.UpdateLayout();Pump(60);
                if(SystemParameters.ClientAreaAnimation)Require(host.ActualHeight>80 && host.ActualHeight<320,"layout resized without intermediate frames: "+host.ActualHeight+", desired "+host.DesiredSize.Height+", animated "+host.HasAnimatedProperties);
                body.Height=120;window.UpdateLayout();Pump(330);Require(Math.Abs(host.ActualHeight-120)<1 && !host.HasAnimatedProperties,"interrupted animation did not settle");
                host.AnimateChanges=false;body.Height=240;window.UpdateLayout();Require(Math.Abs(host.ActualHeight-240)<1,"disabled motion still animates");
            }finally{window.Close();}
        });
        Check("hidden chrome releases its height and dropdowns blend with the panel", delegate {
            var fixture=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(args[0],"tests","view-fixture.json")));var settings=(Dictionary<string,object>)fixture["settings"];settings["density"]=2;settings["opacity"]=65;
            var window=new CompanionWindow(args[0],true);window.UpdateView(fixture);window.Show();window.UpdateLayout();Func<string,object> field=name=>typeof(CompanionWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
            try{
                double height=window.SurfaceHeight;var header=(DockPanel)field("header");var toggle=typeof(CompanionWindow).GetMethod("SetHeaderVisible",BindingFlags.Instance|BindingFlags.NonPublic);
                toggle.Invoke(window,new object[]{false});window.UpdateLayout();Require(!((AnimatedLayout)field("headerReveal")).Expanded && !header.IsHitTestVisible && window.SurfaceHeight<height-30,"hidden header still occupies height");toggle.Invoke(window,new object[]{true});window.UpdateLayout();Require(header.Opacity==1 && header.IsHitTestVisible && Math.Abs(window.SurfaceHeight-height)<1,"header did not restore");
                var visibleTexts=Descendants(window).OfType<TextBlock>().Select(t=>t.Text).ToArray();Require(!visibleTexts.Any(t=>t.Contains("额度已更新") || t.Contains("虚线：")),"removed captions remain visible");Require(visibleTexts.Contains("模型使用量"),"model section was not renamed");
                foreach(string name in new[]{"scope","range"}){var combo=(ComboBox)field(name);Require(((SolidColorBrush)combo.Background).Color.A<80,"selector background is still opaque");combo.ApplyTemplate();combo.IsDropDownOpen=true;window.UpdateLayout();var popup=(System.Windows.Controls.Primitives.Popup)combo.Template.FindName("PART_Popup",combo);var background=((SolidColorBrush)((Border)popup.Child).Background).Color;Require(background.A>100 && background.A<200,"popup does not respect opacity setting");combo.IsDropDownOpen=false;}
            }finally{window.Close();}
        });
        Check("visual collapse keeps the native viewport fixed and the surface fully visible", delegate {
            var fixture=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(args[0],"tests","view-fixture.json")));var settings=(Dictionary<string,object>)fixture["settings"];settings["density"]=2;
            var window=new CompanionWindow(args[0],true);window.UpdateView(fixture);window.Show();window.UpdateLayout();Pump(30);var host=Descendants(window).OfType<AnimatedLayout>().First();host.AnimateChanges=true;
            var surface=(Border)typeof(CompanionWindow).GetField("panel",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);int visibleResizes=0,resizes=0;
            window.SizeChanged+=delegate{resizes++;if(surface.Opacity>.01)visibleResizes++;};
            double minimumOpacity=1;surface.SizeChanged+=delegate{minimumOpacity=Math.Min(minimumOpacity,surface.Opacity);};
            try{double expanded=window.SurfaceHeight;settings["density"]=0;window.UpdateView(fixture);window.UpdateLayout();Pump(550);
                Require(window.SurfaceHeight<expanded-200,"collapse did not finish");
                Require(resizes==0 && visibleResizes==0,"background was resized while visible: "+visibleResizes+" / "+resizes+" size changes");
                Require(minimumOpacity>.999 && Math.Abs(surface.Opacity-1)<.001,"background faded during collapse");
            }finally{window.Close();}
        });
        Check("companion density changes animate through the scroll container", delegate {
            var fixture=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(args[0],"tests","view-fixture.json")));var settings=(Dictionary<string,object>)fixture["settings"];settings["density"]=0;
            var window=new CompanionWindow(args[0],true);window.UpdateView(fixture);window.Show();window.UpdateLayout();Pump(30);var host=Descendants(window).OfType<AnimatedLayout>().First();host.AnimateChanges=true;Require(host.IsLoaded,"companion animation host did not load");
            try{
                var frames=new List<double>();((Border)typeof(CompanionWindow).GetField("panel",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window)).SizeChanged+=delegate{frames.Add(window.SurfaceHeight);};double collapsed=window.SurfaceHeight;settings["density"]=2;window.UpdateView(fixture);window.UpdateLayout();Pump(370);double expanded=window.SurfaceHeight;
                Require(expanded>collapsed+200,"detail content is clipped after expansion");if(SystemParameters.ClientAreaAnimation)Require(frames.Any(height=>height>collapsed+1 && height<expanded-1),"window skipped intermediate sizes: "+String.Join(",",frames));
                settings["density"]=0;window.UpdateView(fixture);window.UpdateLayout();Pump(330);Require(Math.Abs(window.SurfaceHeight-collapsed)<1,"companion did not collapse back to initial size");
            }finally{window.Close();}
        });
        Check("rapid density and header changes settle without leaving a faded surface", delegate {
            var fixture=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(args[0],"tests","view-fixture.json")));var settings=(Dictionary<string,object>)fixture["settings"];settings["density"]=2;
            var window=new CompanionWindow(args[0],true);window.UpdateView(fixture);window.Show();window.UpdateLayout();Pump(30);var host=Descendants(window).OfType<AnimatedLayout>().First();host.AnimateChanges=true;
            var surface=(Border)typeof(CompanionWindow).GetField("panel",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
            try{
                settings["density"]=0;window.UpdateView(fixture);window.UpdateLayout();Pump(35);settings["density"]=1;window.UpdateView(fixture);window.UpdateLayout();Pump(380);
                Require(window.SurfaceHeight>250 && surface.Opacity>.99,"newer expanded content was lost during resize");
                settings["density"]=0;window.UpdateView(fixture);window.UpdateLayout();Pump(115);typeof(CompanionWindow).GetMethod("SetHeaderVisible",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,new object[]{false});window.UpdateLayout();Pump(400);
                Require(window.SurfaceHeight<110 && surface.Opacity>.99 && !surface.HasAnimatedProperties,"header collapse interrupted the surface's recovery");
            }finally{window.Close();}
        });
        Check("hover reveal does not jump quota content relative to its pet anchor", delegate {
            var fixture=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(args[0],"tests","view-fixture.json")));
            ((Dictionary<string,object>)fixture["settings"])["density"]=2;
            var window=new CompanionWindow(args[0],true);window.UpdateView(fixture);window.Show();window.UpdateLayout();Pump(30);
            Func<string,object> field=name=>typeof(CompanionWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
            var toggle=typeof(CompanionWindow).GetMethod("SetHeaderVisible",BindingFlags.Instance|BindingFlags.NonPublic);var host=Descendants(window).OfType<AnimatedLayout>().First();var amount=(TextBlock)field("amount");
            try{
                toggle.Invoke(window,new object[]{false});window.UpdateLayout();foreach(var animation in Descendants(window).OfType<AnimatedLayout>())animation.AnimateChanges=true;
                double before=amount.PointToScreen(new Point()).Y;
                var positions=new List<double>();window.LayoutUpdated+=delegate{if(window.IsVisible && PresentationSource.FromVisual(amount)!=null)positions.Add(amount.PointToScreen(new Point()).Y);};
                toggle.Invoke(window,new object[]{true});window.UpdateLayout();double after=amount.PointToScreen(new Point()).Y;
                Require(Math.Abs(after-before)<.01,"quota jumped by "+(after-before).ToString("0.00")+" pixels before the size animation caught up");
                Pump(320);Require(positions.All(y=>Math.Abs(y-before)<.01),"quota moved during reveal: "+positions.Max(y=>Math.Abs(y-before)).ToString("0.00")+" pixels");
                toggle.Invoke(window,new object[]{false});window.UpdateLayout();Pump(320);Require(positions.All(y=>Math.Abs(y-before)<.01),"quota moved during hover collapse");
            }finally{window.Close();}
        });
        Check("transparent reserved viewport passes native hit testing to the window beneath", delegate {
            var work=SystemParameters.WorkArea;var backdrop=new Window{Left=work.Left+40,Top=work.Top+40,Width=400,Height=500,WindowStyle=WindowStyle.None,Background=Brushes.DarkBlue,Topmost=true,ShowActivated=false};backdrop.Show();
            var fixture=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(args[0],"tests","view-fixture.json")));((Dictionary<string,object>)fixture["settings"])["density"]=0;
            var window=new CompanionWindow(args[0],true){Left=backdrop.Left,Top=backdrop.Top,Height=480};window.UpdateView(fixture);window.Show();window.UpdateLayout();Pump(80);
            try{
                var opaque=window.PointToScreen(new Point(30,window.SurfaceOffset+30));var blank=window.PointToScreen(new Point(30,30));
                var front=new System.Windows.Interop.WindowInteropHelper(window).Handle;var back=new System.Windows.Interop.WindowInteropHelper(backdrop).Handle;
                Require(GetAncestor(WindowFromPoint(new ScreenPoint{X=(int)opaque.X,Y=(int)opaque.Y}),2)==front,"visible panel is not interactive");
                Require(GetAncestor(WindowFromPoint(new ScreenPoint{X=(int)blank.X,Y=(int)blank.Y}),2)==back,"transparent viewport intercepts desktop input");
            }finally{window.Close();backdrop.Close();}
        });
        Check("native attachment keeps body pixel-stationary during header animation", delegate {
            var work=SystemParameters.WorkArea;
            var pet=new Window{Left=work.Left+work.Width/2,Top=work.Bottom-160,Width=120,Height=120,WindowStyle=WindowStyle.None,ShowActivated=false};pet.Show();
            var fixture=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(args[0],"tests","view-fixture.json")));
            ((Dictionary<string,object>)fixture["settings"])["density"]=2;
            var window=new CompanionWindow(args[0],true);window.UpdateView(fixture);window.Show();window.UpdateLayout();
            Func<string,object> field=name=>typeof(CompanionWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
            var follower=field("follower");var type=follower.GetType();var tick=type.GetMethod("Tick");var observe=type.GetMethod("Observe");
            type.GetField("<PetHandle>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(follower,new System.Windows.Interop.WindowInteropHelper(pet).Handle);
            type.GetField("lastDiscovery",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(follower,DateTime.UtcNow.AddMinutes(1));
            var handle=new System.Windows.Interop.WindowInteropHelper(window).Handle;
            var monitors=new List<ScreenRect>();EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,delegate(IntPtr monitor,IntPtr dc,ref ScreenRect rect,IntPtr data){monitors.Add(rect);return true;},IntPtr.Zero);
            var screen=monitors.OrderBy(r=>r.Left).ThenBy(r=>r.Top).First();
            SetWindowPos(new System.Windows.Interop.WindowInteropHelper(pet).Handle,IntPtr.Zero,screen.Right-300,screen.Bottom-250,0,0,0x15);
            SetWindowPos(handle,IntPtr.Zero,screen.Left+200,screen.Top+100,0,0,0x15);Pump(60);
            var toggle=typeof(CompanionWindow).GetMethod("SetHeaderVisible",BindingFlags.Instance|BindingFlags.NonPublic);
            var amount=(TextBlock)field("amount");var positions=new List<double>();double baseline=0;
            var bodyPixels=new List<byte[]>();
            Action follow=delegate{
                observe.Invoke(follower,new object[]{new Dictionary<string,object>{{"pet",new Dictionary<string,object>{{"x",0.0},{"y",2.0},{"width",100.0},{"height",100.0},{"dpr",1.25}}},{"visible",true},{"observedAt",(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds}}});
                tick.Invoke(follower,new object[]{window,handle});
            };
            EventHandler frame=delegate{follow();positions.Add(amount.PointToScreen(new Point()).Y);bodyPixels.Add(BodyPixels(window,amount));};
            try{
                toggle.Invoke(window,new object[]{false});window.UpdateLayout();follow();Pump(40);follow();window.UpdateLayout();baseline=amount.PointToScreen(new Point()).Y;
                foreach(var animation in Descendants(window).OfType<AnimatedLayout>())animation.AnimateChanges=true;
                var beforePixels=BodyPixels(window,amount);CompositionTarget.Rendering+=frame;
                for(int cycle=0;cycle<8;cycle++){toggle.Invoke(window,new object[]{true});window.UpdateLayout();Pump(47+cycle*13);toggle.Invoke(window,new object[]{false});window.UpdateLayout();Pump(59+cycle*11);}Pump(350);
                double movement=positions.Max(y=>Math.Abs(y-baseline));Console.WriteLine("  native header body drift="+movement.ToString("0.000")+"px; dpi="+VisualTreeHelper.GetDpi(window).DpiScaleY);
                Require(movement<.01,"native follow moved quota by "+movement.ToString("0.000")+"px");
                Require(bodyPixels.All(p=>p.SequenceEqual(beforePixels)),"quota raster changes during header reveal despite stable coordinates");
            }finally{CompositionTarget.Rendering-=frame;window.Close();pet.Close();}
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
        Check("unchanged empty series still refreshes the scrolling time axis", delegate {
            var chart=new CreditChart{Start=new DateTime(2026,9,16,10,0,0),End=new DateTime(2026,9,16,11,0,0),ContextKey="account:1h"};
            chart.SetSeries(new List<double?[]>{new double?[48]});chart.Measure(new Size(320,142));chart.Arrange(new Rect(0,0,320,142));chart.UpdateLayout();
            var before=ChartPixels(chart);chart.Start=chart.Start.AddHours(1);chart.End=chart.End.AddHours(1);chart.SetSeries(new List<double?[]>{new double?[48]});chart.UpdateLayout();
            Require(!before.SequenceEqual(ChartPixels(chart)),"time axis still displays the previous window");
        });
        Check("detail panel exposes the updated reset time", delegate {
            var fixture=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(args[0],"tests","view-fixture.json")));
            ((Dictionary<string,object>)fixture["settings"])["density"]=2;
            var window=new CompanionWindow(args[0],true);window.UpdateView(fixture);window.Show();window.UpdateLayout();
            try{Require(Descendants(window).OfType<TextBlock>().Any(t=>t.IsVisible && t.Text.Contains(Convert.ToString(fixture["resetLabel"]))),"reset time is absent from the detail surface");}finally{window.Close();}
        });
        Check("attachment uses only above or below with horizontal edge clamping", delegate {
            var work=new Rect(0,0,1600,900);var pet=new Rect(1450,360,112,122);var placement=new AttachmentPlacement();
            var result=placement.Place(pet,new Size(368,480),work,new[]{pet},false);
            Require(placement.Side<=1,"panel was placed alongside the pet");
            Require(result.Left>=work.Left && result.Right<=work.Right,"horizontal overflow");
            Require(result.Bottom<=pet.Top || result.Top>=pet.Bottom,"height-constrained panel overlaps the pet");
            Require(result.Height<480,"insufficient vertical space must constrain height instead of moving sideways");
        });
        Check("opacity preview retains theme controls and chart data", delegate {
            var fixture=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(args[0],"tests","view-fixture.json")));
            var window=new CompanionWindow(args[0],true);window.UpdateView(fixture);
            Func<string,object> field=name=>typeof(CompanionWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
            var chart=(CreditChart)field("chart");var series=typeof(CreditChart).GetField("target",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(chart);
            var combo=(ComboBox)field("scope");var style=combo.ItemContainerStyle;var surface=(Border)field("panel");var background=surface.Background;
            try{
                var change=typeof(CompanionWindow).GetMethod("ChangeSetting",BindingFlags.Instance|BindingFlags.NonPublic);
                for(int i=0;i<60;i++)change.Invoke(window,new object[]{"opacity",40.0+i});
                Require(Object.ReferenceEquals(style,combo.ItemContainerStyle) && Object.ReferenceEquals(background,surface.Background),"opacity preview rebuilt theme resources");
                Require(Object.ReferenceEquals(series,typeof(CreditChart).GetField("target",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(chart)),"opacity preview reprocessed chart series");
                Require(((SolidColorBrush)background).Color.A==(byte)Math.Round(99*2.55),"latest opacity was not previewed");
            }finally{window.Close();}
        });
        Check("slider persistence coalesces changes and flushes the final value", delegate {
            var messages=new List<Dictionary<string,object>>();var committer=new SettingsCommitter(message=>messages.Add(message));
            for(int i=0;i<120;i++)committer.Set("opacity",40+(i%61));
            Require(messages.Count==0,"slider drag persisted every preview");Pump(300);
            Require(messages.Count==1 && Convert.ToInt32(messages[0]["opacity"])==98,"debounced commit did not keep the latest value");
            committer.Set("opacity",75);committer.Dispose();Pump(230);
            Require(messages.Count==2 && Convert.ToInt32(messages[1]["opacity"])==75,"close lost or duplicated the final setting");
        });
        Check("backend transport preserves command order and drains settings on shutdown", delegate {
            string root=Path.Combine(args[0],"artifacts","transport-test",Guid.NewGuid().ToString("N"));string backendPath=Path.Combine(root,"src","backend");Directory.CreateDirectory(backendPath);
            // Publish the trace atomically: concurrent ReadAllLines would deny the Node writer on Windows.
            File.WriteAllText(Path.Combine(backendPath,"main.mjs"),"import fs from 'node:fs';import readline from 'node:readline';const seen=[];readline.createInterface({input:process.stdin}).on('line',line=>{const m=JSON.parse(line);seen.push(m);if(m.type==='shutdown'){fs.writeFileSync('commands.tmp',JSON.stringify(seen));fs.renameSync('commands.tmp','commands.json');process.exit(0);}else console.log(JSON.stringify({type:'settings',settings:m}));});");
            var received=new System.Collections.Concurrent.ConcurrentQueue<int>();var client=new BackendClient();
            client.SettingsReceived+=message=>received.Enqueue(Convert.ToInt32(Json.Get(Json.Get(message,"settings"),"sequence")));
            try{
                client.Start(root);for(int i=0;i<120;i++)client.Send(new Dictionary<string,object>{{"type","settings"},{"sequence",i}});
                var clock=System.Diagnostics.Stopwatch.StartNew();while(received.Count<120 && clock.ElapsedMilliseconds<4000)Pump(20);
                Require(received.ToArray().SequenceEqual(Enumerable.Range(0,120)),"protocol writer reordered or lost settings");
                client.Send(new Dictionary<string,object>{{"type","settings"},{"sequence",120}});client.Dispose();
                string path=Path.Combine(root,"commands.json");while(clock.ElapsedMilliseconds<5000 && !File.Exists(path))Pump(20);
                Require(File.Exists(path),"shutdown command was not delivered");
                var commands=Json.Items(Json.Serializer.DeserializeObject(File.ReadAllText(path))).ToArray();
                Require(commands.Length==122 && Json.Number(commands[120],"sequence")==120 && Json.Text(commands[121],"type")=="shutdown","shutdown bypassed pending settings");
            }finally{client.Dispose();}
        });
        return failures == 0 ? 0 : 1;
    }
    private static void Pump(int milliseconds) { var frame=new System.Windows.Threading.DispatcherFrame();var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(milliseconds)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();System.Windows.Threading.Dispatcher.PushFrame(frame); }
    private static byte[] ChartPixels(CreditChart chart) {
        chart.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Render,new Action(delegate{}));
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(320,142,96,96,PixelFormats.Pbgra32);bitmap.Render(chart);
        var pixels=new byte[320*142*4];bitmap.CopyPixels(pixels,320*4,0);return pixels;
    }
    private static byte[] BodyPixels(CompanionWindow window,TextBlock amount) {
        var dpi=VisualTreeHelper.GetDpi(window);var point=amount.TranslatePoint(new Point(),window);
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(window.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);
        bitmap.Render(window);int width=(int)(amount.ActualWidth*dpi.DpiScaleX),height=(int)(amount.ActualHeight*dpi.DpiScaleY);var pixels=new byte[width*height*4];
        bitmap.CopyPixels(new Int32Rect((int)Math.Round(point.X*dpi.DpiScaleX),(int)Math.Round(point.Y*dpi.DpiScaleY),width,height),pixels,width*4,0);return pixels;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var next in Descendants(child)) yield return next; }
    }
    private static IEnumerable<Drawing> Drawings(Drawing drawing) {
        yield return drawing;var group=drawing as DrawingGroup;
        if(group!=null)foreach(var child in group.Children)foreach(var descendant in Drawings(child))yield return descendant;
    }
}
