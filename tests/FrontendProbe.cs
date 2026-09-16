using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CodexPetCredits;

// Exercises our real popup/window stack beside the live pet; it never injects mouse input.
public static class FrontendProbe {
    [StructLayout(LayoutKind.Sequential)] public struct NativePoint { public int X,Y; }
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window,uint flags);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [STAThread] public static int Main(string[] args) {
        var app = new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        var json = new JavaScriptSerializer();
        var fixture = (Dictionary<string,object>)json.DeserializeObject(File.ReadAllText(Path.Combine(args[0],"tests","view-fixture.json")));
        ((Dictionary<string,object>)fixture["settings"])["density"]=1;
        var pet = (Dictionary<string,object>)json.DeserializeObject(File.ReadAllText(args[1]));
        var geometry=(Dictionary<string,object>)pet["pet"];double baseX=Convert.ToDouble(geometry["x"]),baseY=Convert.ToDouble(geometry["y"]);
        var window = new CompanionWindow(args[0],true); window.UpdateView(fixture);window.Show();window.UpdateLayout();
        var field = typeof(CompanionWindow).GetField("follower",BindingFlags.Instance|BindingFlags.NonPublic);
        var follower=field.GetValue(window);var observe=follower.GetType().GetMethod("Observe");var tick=follower.GetType().GetMethod("Tick");
        var handle=new WindowInteropHelper(window).Handle;
        var panel=(Border)typeof(CompanionWindow).GetField("panel",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
        var animated=(AnimatedLayout)((ScrollViewer)panel.Child).Content;animated.AnimateChanges=true;
        bool watchCollapse=false;int visibleCollapseResizes=0;
        window.SizeChanged+=delegate{if(watchCollapse && panel.Opacity>.01)visibleCollapseResizes++;};
        var samples=new List<double>();var costs=new List<double>();var clock=Stopwatch.StartNew();double last=0;
        var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(150)};
        int count=0;bool checkedPopup=false,popupPass=false,rangePass=false;
        TimeSpan rendered=TimeSpan.MinValue;
        EventHandler frame = null;
        frame=delegate(object sender,EventArgs args2) {
            var rendering=args2 as RenderingEventArgs;if(rendering!=null){if(rendering.RenderingTime==rendered || !window.IsVisible)return;rendered=rendering.RenderingTime;}
            double now=clock.Elapsed.TotalMilliseconds;if(last>0 && count>5)samples.Add(now-last);last=now;
            pet["observedAt"]=(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds;
            // Replay a moving anchor without moving the user's cursor or the real pet.
            if(count>=30){geometry["x"]=baseX+Math.Sin((count-30)*.12)*95;geometry["y"]=baseY+Math.Sin((count-30)*.07)*40;}
            observe.Invoke(follower,new object[]{pet});var cost=Stopwatch.StartNew();tick.Invoke(follower,new object[]{window,handle});costs.Add(cost.Elapsed.TotalMilliseconds);
            if(++count==8){var scope=(ComboBox)typeof(CompanionWindow).GetField("scope",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);scope.IsDropDownOpen=true;scope.UpdateLayout();}
            if(count==14){
                var scope=(ComboBox)typeof(CompanionWindow).GetField("scope",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
                var item=scope.ItemContainerGenerator.ContainerFromIndex(1) as ComboBoxItem;
                var popup=(Popup)scope.Template.FindName("PART_Popup",scope);
                if(item!=null && popup!=null && popup.Child!=null){var p=item.PointToScreen(new Point(item.ActualWidth/2,item.ActualHeight/2));
                    var popupHandle=((HwndSource)PresentationSource.FromVisual(popup.Child)).Handle;
                    var hit=GetAncestor(WindowFromPoint(new NativePoint{X=(int)p.X,Y=(int)p.Y}),2);uint pid;GetWindowThreadProcessId(hit,out pid);
                    Console.WriteLine("popup item="+item.ActualWidth+"x"+item.ActualHeight+"; hit PID="+pid+"; probe PID="+Process.GetCurrentProcess().Id+"; popup topmost="+(hit==popupHandle));
                    popupPass=hit==popupHandle;
                    item.IsSelected=true;popupPass=popupPass && scope.SelectedIndex==1;
                } else Console.WriteLine("popup containers missing: item="+(item!=null)+" popup="+(popup!=null));
                checkedPopup=true;scope.IsDropDownOpen=false;
            }
            if(count==18){var range=(ComboBox)typeof(CompanionWindow).GetField("range",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);range.IsDropDownOpen=true;range.UpdateLayout();}
            if(count==24){var range=(ComboBox)typeof(CompanionWindow).GetField("range",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);var item=range.ItemContainerGenerator.ContainerFromIndex(3) as ComboBoxItem;var popup=(Popup)range.Template.FindName("PART_Popup",range);
                if(item!=null && popup!=null){var p=item.PointToScreen(new Point(item.ActualWidth/2,item.ActualHeight/2));var popupHandle=((HwndSource)PresentationSource.FromVisual(popup.Child)).Handle;rangePass=GetAncestor(WindowFromPoint(new NativePoint{X=(int)p.X,Y=(int)p.Y}),2)==popupHandle;item.IsSelected=true;rangePass=rangePass && range.SelectedItem.ToString()=="7d";}range.IsDropDownOpen=false;
            }
            if(count==40){((Dictionary<string,object>)fixture["settings"])["density"]=2;window.UpdateView(fixture);}
            if(count==64){watchCollapse=true;((Dictionary<string,object>)fixture["settings"])["density"]=0;window.UpdateView(fixture);}
            if(count>=105){timer.Stop();CompositionTarget.Rendering-=frame;samples.Sort();costs.Sort();Console.WriteLine("frame interval median="+samples[samples.Count/2].ToString("0.0")+"ms p95="+samples[(int)(samples.Count*.95)].ToString("0.0")+"ms; follow cost p95="+costs[(int)(costs.Count*.95)].ToString("0.00")+"ms");bool collapsePass=window.ActualHeight<180 && panel.Opacity>.99 && (!SystemParameters.ClientAreaAnimation || visibleCollapseResizes==0);Console.WriteLine("collapse visible resizes="+visibleCollapseResizes+"; surface opacity="+panel.Opacity.ToString("0.00"));bool passed=checkedPopup&&popupPass&&rangePass&&collapsePass;Console.WriteLine((passed?"PASS":"FAIL")+" menus, moving-anchor replay and hidden-background collapse completed");window.Close();app.Shutdown(passed?0:1);}
        };
        CompositionTarget.Rendering+=frame;timer.Tick+=delegate{if(!window.IsVisible)frame(null,EventArgs.Empty);};
        timer.Start();return app.Run();
    }
}
