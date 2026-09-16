using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows;
using CodexPetCredits;

// Compare repeatable UI-thread work; these timings are not display frame-rate claims.
public static class FrontendPerformance {
    private static void Measure(string name,Action action,int count) {
        for(int i=0;i<20;i++)action();
        var samples=new List<double>();
        for(int run=0;run<5;run++){var watch=Stopwatch.StartNew();for(int i=0;i<count;i++)action();samples.Add(watch.Elapsed.TotalMilliseconds);}
        samples.Sort();Console.WriteLine(name+": "+count+" updates median="+samples[2].ToString("0.00")+"ms");
    }
    [STAThread] public static void Main(string[] args) {
        new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        var fixture=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(args[0],"tests","view-fixture.json")));
        ((Dictionary<string,object>)fixture["settings"])["density"]=2;
        var window=new CompanionWindow(args[0],true);window.UpdateView(fixture);window.Show();window.UpdateLayout();
        try{
            var method=typeof(CompanionWindow).GetMethod("ChangeSetting",BindingFlags.Instance|BindingFlags.NonPublic);int value=40;
            Measure("Opacity preview",delegate{method.Invoke(window,new object[]{"opacity",(double)(40+(value++%61))});},120);
            var chart=(CreditChart)typeof(CompanionWindow).GetField("chart",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
            chart.SelectBucket(12);Measure("Same-bucket hover",delegate{chart.SelectBucket(12);},10000);
        }finally{window.Close();}
    }
}
