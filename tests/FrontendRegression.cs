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
                foreach (var text in texts) Require(((SolidColorBrush)text.Foreground).Color.R < 100,"legend retains pale dark-theme text");
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
        return failures == 0 ? 0 : 1;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var next in Descendants(child)) yield return next; }
    }
}
