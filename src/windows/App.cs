using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Markup;
using System.Windows.Threading;

namespace CodexPetCredits {
    internal static class Json {
        public static JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        public static Dictionary<string, object> Map(object value) { return value as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        public static object Get(object value, string key) { object result; return Map(value).TryGetValue(key, out result) ? result : null; }
        public static string Text(object value, string key, string fallback = "") { return Get(value, key) == null ? fallback : Convert.ToString(Get(value, key)); }
        public static double Number(object value, string key, double fallback = 0) { try { var v = Get(value, key); return v == null ? fallback : Convert.ToDouble(v); } catch { return fallback; } }
        public static bool Flag(object value, string key) { return Get(value, key) is bool && (bool)Get(value, key); }
        public static IEnumerable<object> Items(object value) { return (value as IEnumerable ?? new object[0]).Cast<object>(); }
    }

    public sealed class CompanionWindow : Window {
        private readonly string root;
        private readonly bool testMode;
        private Process backend;
        private IntPtr hwnd, hook;
        private Native.WinEventProc hookCallback;
        private readonly DispatcherTimer hiddenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        private readonly AttachmentFollower follower = new AttachmentFollower();
        private TimeSpan lastRendering;
        private readonly object inbox = new object();
        private object queuedPet;
        private bool petQueued, updating, dark = true;
        private int density;
        private string accent = "mint", detailSignature = "";
        private object state;
        private Dictionary<string, object> settings = new Dictionary<string, object>();
        private readonly Dictionary<string, object> pendingSettings = new Dictionary<string, object>();
        private readonly Border panel = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Padding = new Thickness(13) };
        private readonly StackPanel layout = new StackPanel(), trend = new StackPanel(), detail = new StackPanel(), preferences = new StackPanel();
        private readonly StackPanel legendRows = new StackPanel(), modelRows = new StackPanel(), accountRows = new StackPanel();
        private readonly List<TextBlock> labels = new List<TextBlock>(), values = new List<TextBlock>();
        private readonly List<Border> cards = new List<Border>();
        private readonly List<Button> modeButtons = new List<Button>(), colorButtons = new List<Button>();
        private readonly ComboBox scope = new ComboBox(), range = new ComboBox();
        private readonly CreditChart chart = new CreditChart();
        private readonly Slider opacity = new Slider { Minimum = 40, Maximum = 100, Value = 90, Width = 145 };
        private readonly Popup titlePopup = new Popup { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom };
        private TextBlock amount, quotaLabel, forecast, forecastLabel, subTitle, total, totalLabel, intervalLabel, recentHour, recentDay, reset, status, coverage;
        private Button smoothButton, themeButton;
        private Expander modelsDisclosure, accountsDisclosure;
        private Brush primary, muted, accentBrush;

        public CompanionWindow(string projectRoot, bool testing) {
            root = projectRoot; testMode = testing;
            Title = "Codex Pet Credits"; Width = 320; SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
            Topmost = true; ShowInTaskbar = false; ShowActivated = false; FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 11;
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            Content = panel;
            panel.Child = new ScrollViewer { Content = layout, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            BuildLayout(); ApplySettings();
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if(e.Key == Key.Escape) { titlePopup.IsOpen = false; preferences.Visibility = Visibility.Collapsed; ChangeSetting("density",0); } };
            SourceInitialized += OnSourceInitialized;
            Closed += delegate {
                hiddenTimer.Stop(); CompositionTarget.Rendering -= OnRendering; titlePopup.IsOpen = false;
                if(hook != IntPtr.Zero)Native.UnhookWinEvent(hook);
                Send(new Dictionary<string,object>{{"type","shutdown"}});
                if(backend != null)try{backend.StandardInput.Close();}catch{}
            };
        }
        private TextBlock Text(string text, double size, bool value = false) {
            var label = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            if(value){label.FontWeight = FontWeights.SemiBold;values.Add(label);}else labels.Add(label);
            return label;
        }
        private Border Card(UIElement child, Thickness margin) {
            var card = new Border { Child = child, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Padding = new Thickness(10,7,10,7), Margin = margin };
            cards.Add(card); return card;
        }
        private Button ActionButton(string text,string hint,Action action,double width = 27) {
            var button = new Button { Content = text, ToolTip = hint, Width = width, Height = 26, Padding = new Thickness(5,0,5,0), Cursor = Cursors.Hand, Background = Brushes.Transparent, Foreground = muted ?? Brushes.Gray, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1), HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(2,0,0,0) };
            button.Template = (ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='Frame' CornerRadius='6' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Frame' Property='Opacity' Value='0.75'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='#879AA8'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
            AutomationProperties.SetName(button,hint);button.Click += delegate { action(); };return button;
        }
        private void BuildLayout() {
            var header = new DockPanel { Margin = new Thickness(0,0,0,9) };
            var modes = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(modes,Dock.Right); header.Children.Add(modes);
            var icons = new[]{"−","∿","≡"};
            string[] paths={"M 1,6 L 12,6","M 1,10 L 4,5 L 7,8 L 12,2","M 2,2 L 12,2 M 2,6 L 12,6 M 2,10 L 9,10"};
            for(int i=0;i<3;i++){int mode=i;var button=ActionButton(icons[i],new[]{"极简","趋势","详细"}[i],delegate{ChangeSetting("density",density==mode?0:mode);});button.Content=new System.Windows.Shapes.Path{Data=Geometry.Parse(paths[i]),Stroke=Brushes.Gray,StrokeThickness=1.3,Width=12,Height=12,Stretch=Stretch.Uniform};modeButtons.Add(button);modes.Children.Add(button);}
            modes.Children.Add(ActionButton("⋯","外观设置",delegate{preferences.Visibility=preferences.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;}));
            header.Children.Add(Text("CODEX  /  用量",10));layout.Children.Add(header);
            var summary = new Grid();summary.ColumnDefinitions.Add(new ColumnDefinition());summary.ColumnDefinitions.Add(new ColumnDefinition());
            var quota = new StackPanel();amount=Text("—",28,true);quotaLabel=Text("周额度剩余",10);quota.Children.Add(amount);quota.Children.Add(quotaLabel);
            summary.Children.Add(Card(quota,new Thickness(0,0,4,0)));
            var prediction = new StackPanel();forecast=Text("学习中",16,true);forecast.MinHeight=36;forecastLabel=Text("耗尽预测",10);prediction.Children.Add(forecast);prediction.Children.Add(forecastLabel);
            var predictionCard=Card(prediction,new Thickness(4,0,0,0));Grid.SetColumn(predictionCard,1);summary.Children.Add(predictionCard);layout.Children.Add(summary);
            layout.Children.Add(trend);trend.Margin=new Thickness(0,10,0,0);
            var filters = new DockPanel();
            ConfigureCombo(range,new[]{"1h","6h","24h","7d","30d"},72,"时间窗口");
            ConfigureCombo(scope,new[]{"当前任务","本机汇总","主要任务","主要项目"},112,"统计范围");
            DockPanel.SetDock(range,Dock.Right);filters.Children.Add(range);filters.Children.Add(scope);trend.Children.Add(filters);
            scope.SelectionChanged+=delegate{if(!updating && scope.SelectedIndex>=0)ChangeSetting("scope",new[]{"current","account","tasks","projects"}[scope.SelectedIndex]);};
            range.SelectionChanged+=delegate{if(!updating && range.SelectedItem!=null)ChangeSetting("range",range.SelectedItem.ToString());};
            subTitle=Text("正在读取任务",10);subTitle.MaxHeight=18;
            var titleButton=ActionButton("","查看完整名称",delegate{ShowTitle(subTitle.Tag as string ?? subTitle.Text);},Double.NaN);
            titleButton.Content=subTitle;titleButton.HorizontalContentAlignment=HorizontalAlignment.Left;titleButton.Height=23;titleButton.Padding=new Thickness(0);titleButton.Margin=new Thickness(0,3,0,3);titleButton.BorderThickness=new Thickness(0);
            titlePopup.PlacementTarget=titleButton;trend.Children.Add(titleButton);
            var totalStack=new StackPanel();total=Text("— cr",23,true);totalLabel=Text("所选时段 · 估算消耗合计",10);totalStack.Children.Add(total);totalStack.Children.Add(totalLabel);
            trend.Children.Add(Card(totalStack,new Thickness(0,0,0,7)));
            var chartHeader=new DockPanel();
            smoothButton=ActionButton("平滑","切换平滑趋势或区间原值",delegate{ChangeSetting("smoothing",Json.Text(settings,"smoothing","smooth")=="smooth"?"raw":"smooth");},48);
            smoothButton.Height=23;DockPanel.SetDock(smoothButton,Dock.Right);chartHeader.Children.Add(smoothButton);
            intervalLabel=Text("估算 credits / 区间",10);chartHeader.Children.Add(intervalLabel);trend.Children.Add(chartHeader);
            chart.Height=108;trend.Children.Add(chart);legendRows.Margin=new Thickness(0,3,0,0);trend.Children.Add(legendRows);
            layout.Children.Add(detail);detail.Margin=new Thickness(0,10,0,0);
            var recent = new Grid();recent.ColumnDefinitions.Add(new ColumnDefinition());recent.ColumnDefinitions.Add(new ColumnDefinition());
            var hourStack=new StackPanel();recentHour=Text("—",19,true);hourStack.Children.Add(recentHour);hourStack.Children.Add(Text("本机近 1h · 估算 cr",10));
            recent.Children.Add(Card(hourStack,new Thickness(0,0,4,0)));
            var dayStack=new StackPanel();recentDay=Text("—",19,true);dayStack.Children.Add(recentDay);dayStack.Children.Add(Text("本机近 24h · 估算 cr",10));
            var dayCard=Card(dayStack,new Thickness(4,0,0,0));Grid.SetColumn(dayCard,1);recent.Children.Add(dayCard);detail.Children.Add(recent);
            modelsDisclosure=Disclosure("模型构成 · 所选时段",modelRows);detail.Children.Add(modelsDisclosure);
            accountsDisclosure=Disclosure("其他额度",accountRows);detail.Children.Add(accountsDisclosure);
            reset=Text("",10);reset.Margin=new Thickness(0,7,0,0);detail.Children.Add(reset);
            status=Text("正在连接",10);status.Margin=new Thickness(0,4,0,0);detail.Children.Add(status);
            preferences.Visibility=Visibility.Collapsed;preferences.Margin=new Thickness(0,12,0,0);layout.Children.Add(preferences);
            preferences.Children.Add(Text("主题色",10));var swatches=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,6,0,10)};
            for(int i=0;i<Palette.Keys.Length;i++){int index=i;var button=ActionButton("",Palette.Names[i],delegate{ChangeSetting("accent",Palette.Keys[index]);},40);colorButtons.Add(button);swatches.Children.Add(button);}
            preferences.Children.Add(swatches);
            var appearance=new DockPanel();themeButton=ActionButton("深色","切换浅色或深色",delegate{ChangeSetting("theme",dark?"light":"dark");},54);DockPanel.SetDock(themeButton,Dock.Right);appearance.Children.Add(themeButton);appearance.Children.Add(Text("背景不透明度",10));appearance.Children.Add(opacity);preferences.Children.Add(appearance);
            AutomationProperties.SetName(opacity,"背景不透明度");
            opacity.ValueChanged+=delegate{if(!updating)ChangeSetting("opacity",opacity.Value);};
            coverage=Text("",10);coverage.TextWrapping=TextWrapping.Wrap;coverage.TextTrimming=TextTrimming.None;coverage.MaxHeight=170;
            preferences.Children.Add(Disclosure("数据与预测说明",new ScrollViewer{Content=coverage,MaxHeight=170,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}));
            var menu=new ContextMenu();
            foreach(var entry in new[]{"极简","趋势","详细","刷新额度","退出挂件"}){string action=entry;var item=new MenuItem{Header=entry};item.Click+=delegate{int mode=Array.IndexOf(new[]{"极简","趋势","详细"},action);if(mode>=0)ChangeSetting("density",mode);else if(action=="刷新额度")Send(new Dictionary<string,object>{{"type","refresh"}});else Close();};menu.Items.Add(item);}panel.ContextMenu=menu;
        }
        private void ConfigureCombo(ComboBox combo,string[] items,double width,string name) {
            foreach(var item in items)combo.Items.Add(item);
            combo.Width=width;combo.Height=27;combo.FontSize=11;combo.HorizontalAlignment=HorizontalAlignment.Left;
            AutomationProperties.SetName(combo,name);
            combo.Template=(ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ComboBox'><Grid><ToggleButton Focusable='False' IsChecked='{Binding IsDropDownOpen,Mode=TwoWay,RelativeSource={RelativeSource TemplatedParent}}' ClickMode='Press' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'><ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='6'><Path Data='M 0,0 L 4,4 L 8,0' Stroke='#87949F' StrokeThickness='1.2' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,8,0'/></Border></ControlTemplate></ToggleButton.Template></ToggleButton><ContentPresenter Content='{TemplateBinding SelectionBoxItem}' IsHitTestVisible='False' Margin='9,0,23,0' VerticalAlignment='Center'/><Popup x:Name='PART_Popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}' AllowsTransparency='True' Focusable='False'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' Padding='3' CornerRadius='6' MinWidth='{TemplateBinding ActualWidth}'><ScrollViewer CanContentScroll='True'><ItemsPresenter x:Name='ItemsPresenter'/></ScrollViewer></Border></Popup></Grid><ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter Property='BorderBrush' Value='#879AA8'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
        }
        private Expander Disclosure(string title,UIElement content) {
            var expander=new Expander{Header=Text(title,10),Content=content,Margin=new Thickness(0,9,0,0)};
            expander.Template=(ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Expander'><StackPanel><ToggleButton IsChecked='{Binding IsExpanded,Mode=TwoWay,RelativeSource={RelativeSource TemplatedParent}}' Content='{TemplateBinding Header}' Cursor='Hand'><ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border Background='Transparent' Padding='0,4'><DockPanel><Path x:Name='Chevron' Data='M 0,0 L 4,4 L 0,8' Stroke='#87949F' StrokeThickness='1.2' Margin='2,0,9,0' VerticalAlignment='Center' RenderTransformOrigin='0.5,0.5'/><ContentPresenter/></DockPanel></Border><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='Chevron' Property='RenderTransform'><Setter.Value><RotateTransform Angle='90'/></Setter.Value></Setter></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter Property='Opacity' Value='0.7'/></Trigger></ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template></ToggleButton><ContentPresenter x:Name='Body' Content='{TemplateBinding Content}' Visibility='Collapsed'/></StackPanel><ControlTemplate.Triggers><Trigger Property='IsExpanded' Value='True'><Setter TargetName='Body' Property='Visibility' Value='Visible'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
            return expander;
        }
        private void ShowTitle(string title) {
            var text=new TextBox{Text=title,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=150,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,BorderThickness=new Thickness(0),Padding=new Thickness(12),FontSize=12,Foreground=primary,Background=new SolidColorBrush(dark?Color.FromRgb(34,37,44):Color.FromRgb(246,246,250))};
            titlePopup.Child=new Border{Child=text,Width=300,CornerRadius=new CornerRadius(9)};titlePopup.IsOpen=!titlePopup.IsOpen;
        }
        private void ChangeSetting(string key,object value) {
            settings[key]=value;if(!testMode)pendingSettings[key]=value;
            ApplySettings();UpdateContent();Send(new Dictionary<string,object>{{"type","settings"},{key,value}});
        }
        private void ApplySettings() {
            updating=true;density=(int)Json.Number(settings,"density");dark=Json.Text(settings,"theme","dark")!="light";accent=Json.Text(settings,"accent","mint");
            Width=density==0?320:352;trend.Visibility=density>=1?Visibility.Visible:Visibility.Collapsed;detail.Visibility=density==2?Visibility.Visible:Visibility.Collapsed;
            scope.SelectedIndex=Math.Max(0,Array.IndexOf(new[]{"current","account","tasks","projects"},Json.Text(settings,"scope","current")));range.SelectedItem=Json.Text(settings,"range","24h");
            opacity.Value=Json.Number(settings,"opacity",90);themeButton.Content=dark?"深色":"浅色";smoothButton.Content=Json.Text(settings,"smoothing","smooth")=="smooth"?"平滑":"原值";
            ApplyTheme();updating=false;
        }
        private void ApplyTheme() {
            byte alpha=(byte)Math.Round(opacity.Value*2.55);
            panel.Background=new SolidColorBrush(dark?Color.FromArgb(alpha,29,32,39):Color.FromArgb(alpha,248,248,251));
            panel.BorderBrush=new SolidColorBrush(dark?Color.FromRgb(64,68,80):Color.FromRgb(211,212,222));
            primary=new SolidColorBrush(dark?Color.FromRgb(224,226,234):Color.FromRgb(49,52,65));
            muted=new SolidColorBrush(dark?Color.FromRgb(160,165,181):Color.FromRgb(103,108,126));
            accentBrush=new SolidColorBrush(Palette.Accent(accent,dark));Foreground=primary;
            foreach(var label in labels)label.Foreground=muted;foreach(var value in values)value.Foreground=primary;amount.Foreground=accentBrush;
            var tint=Palette.Accent(accent,dark);
            foreach(var card in cards){card.Background=new SolidColorBrush(Color.FromArgb(dark?(byte)14:(byte)12,tint.R,tint.G,tint.B));card.BorderBrush=new SolidColorBrush(dark?Color.FromRgb(61,65,77):Color.FromRgb(220,221,229));}
            foreach(var button in modeButtons){button.Foreground=muted;button.BorderBrush=Brushes.Transparent;button.Background=Brushes.Transparent;}
            modeButtons[density].Foreground=accentBrush;modeButtons[density].Background=new SolidColorBrush(Color.FromArgb(25,tint.R,tint.G,tint.B));
            foreach(var button in modeButtons)((System.Windows.Shapes.Path)button.Content).Stroke=button.Foreground;
            smoothButton.Foreground=muted;smoothButton.BorderBrush=panel.BorderBrush;themeButton.Foreground=primary;themeButton.BorderBrush=panel.BorderBrush;
            for(int i=0;i<colorButtons.Count;i++){var button=colorButtons[i];button.Content=new System.Windows.Shapes.Ellipse{Width=13,Height=13,Fill=new SolidColorBrush(Palette.Accent(Palette.Keys[i],dark))};button.BorderBrush=Palette.Keys[i]==accent?accentBrush:Brushes.Transparent;}
            foreach(var combo in new[]{scope,range}){
                combo.Foreground=primary;combo.Background=new SolidColorBrush(dark?Color.FromRgb(37,40,49):Color.FromRgb(240,240,246));combo.BorderBrush=panel.BorderBrush;
                var itemStyle=new Style(typeof(ComboBoxItem));itemStyle.Setters.Add(new Setter(Control.ForegroundProperty,primary));itemStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(8,7,8,7)));combo.ItemContainerStyle=itemStyle;
            }
            chart.Dark=dark;chart.Accent=accent;chart.InvalidateVisual();
        }
        public void UpdateView(object data) {
            state=data;var received=new Dictionary<string,object>(Json.Map(Json.Get(data,"settings")));
            // A delayed view must not undo a menu choice while its settings acknowledgement is in flight.
            foreach(var entry in pendingSettings.ToArray()){if(Json.Text(received,entry.Key)==Convert.ToString(entry.Value))pendingSettings.Remove(entry.Key);else received[entry.Key]=entry.Value;}
            string oldSettings=Json.Serializer.Serialize(settings);settings=received;
            if(oldSettings!=Json.Serializer.Serialize(settings))ApplySettings();
            UpdateContent();
        }
        private static string Credit(object data,string key) { return Json.Get(data,key)==null?"—":Json.Number(data,key).ToString("N1"); }
        private void UpdateContent() {
            if(state==null)return;
            amount.Text=Json.Get(state,"remaining")==null?"—":Json.Number(state,"remaining").ToString("0")+"%";
            quotaLabel.Text=Json.Text(state,"quotaLabel").Contains("周")?"周额度剩余":"额度剩余";
            string prediction=Json.Text(state,"forecast","正在学习");
            forecast.Text=prediction=="正在学习"?"学习中":prediction=="预计可用至重置"?"可用至重置":prediction.Replace("预计 ","").Replace(" 耗尽","");
            forecast.FontSize=forecast.Text.Length>13?13:16;
            forecastLabel.Text=Json.Flag(state,"warning")?"!  预计提前耗尽":"耗尽时间预测";
            forecast.ToolTip="结合工作习惯与近期速度";
            string title=Json.Text(settings,"scope","current")=="current"?Json.Text(state,"taskTitle","暂无任务"):Json.Text(state,"subtitle","本机已记录");
            subTitle.Tag=title;subTitle.Text=UiText.Short(title,30);
            total.Text=Credit(state,"total")+" cr";
            totalLabel.Text=Json.Text(state,"totalLabel","所选范围 · 所选时段合计")+" · 估算";
            totalLabel.ToolTip="图中曲线在所选时段内的消耗合计";
            double minutes=(Json.Number(state,"windowEnd")-Json.Number(state,"windowStart"))/48/60000;
            intervalLabel.Text="credits / "+(minutes<60?minutes.ToString("0.##")+" 分钟":(minutes/60).ToString("0.##")+" 小时");
            var series=Json.Items(Json.Get(state,"series")).ToArray();
            chart.Names=series.Select(s=>Json.Text(s,"name")).ToArray();
            chart.Smooth=Json.Text(settings,"smoothing","smooth")=="smooth";
            chart.ContextKey=Json.Text(state,"chartKey",Json.Text(settings,"scope")+":"+Json.Text(settings,"range")+":"+String.Join("|",chart.Names))+":"+chart.Smooth;
            chart.Start=Epoch(Json.Number(state,"windowStart"));chart.End=Epoch(Json.Number(state,"windowEnd"));
            chart.SetSeries(series.Select(s=>Json.Items(Json.Get(s,"points")).Select(v=>v==null?(double?)null:Convert.ToDouble(v)).ToArray()).ToList());
            recentHour.Text=Credit(Json.Get(state,"recentCredits"),"hour");recentDay.Text=Credit(Json.Get(state,"recentCredits"),"day");
            reset.Text="额度重置  "+Json.Text(state,"resetLabel");
            status.Text=Json.Get(state,"updated")==null?"等待额度数据":Json.Number(state,"updated")>300?"额度更新暂停":"额度已更新 · 消耗来自本机";
            coverage.Text=Json.Text(state,"coverage")+"\n\n"+Json.Text(state,"forecastDetail")+"\n\n平滑仅影响曲线形状；悬停显示区间原值，合计保持原始估算。\n费率 "+Json.Text(state,"rateVersion");
            string signature=Json.Serializer.Serialize(new object[]{series.Select(s=>new[]{Json.Text(s,"name"),Credit(s,"total")}),Json.Get(state,"details"),Json.Get(state,"other"),Json.Get(state,"officialTaskCredits"),dark,accent});
            if(signature==detailSignature)return;detailSignature=signature;
            legendRows.Children.Clear();
            if(series.Length>1)for(int i=0;i<Math.Min(3,series.Length);i++)AddRow(legendRows,Json.Text(series[i],"name"),Credit(series[i],"total")+" cr",Palette.Series(accent,dark,i));
            modelRows.Children.Clear();
            foreach(var model in Json.Items(Json.Get(state,"details")).Take(5))AddRow(modelRows,Json.Text(model,"name"),Json.Text(model,"value"),null);
            if(modelRows.Children.Count==0)AddRow(modelRows,"暂无模型记录","—",null);
            accountRows.Children.Clear();foreach(var row in Json.Items(Json.Get(state,"otherQuotas")))AddRow(accountRows,Json.Text(row,"name"),Json.Text(row,"value"),null);
            if(Json.Get(state,"officialTaskCredits")!=null)AddRow(accountRows,"当前任务 · 服务端累计",Credit(state,"officialTaskCredits")+" cr",null);
            accountsDisclosure.Visibility=accountRows.Children.Count==0?Visibility.Collapsed:Visibility.Visible;
        }
        private void AddRow(StackPanel target,string title,string value,Color? color) {
            var row=new Grid{Margin=new Thickness(0,4,0,0)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var name=new TextBlock{Text=UiText.Short(title,26),FontSize=10,Foreground=muted,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};
            var titleButton=ActionButton("","查看完整名称",delegate{ShowTitle(title);},Double.NaN);titleButton.Content=name;titleButton.Height=19;titleButton.Padding=new Thickness(color.HasValue?12:0,0,0,0);titleButton.HorizontalContentAlignment=HorizontalAlignment.Left;titleButton.BorderThickness=new Thickness(0);row.Children.Add(titleButton);
            if(color.HasValue)row.Children.Add(new System.Windows.Shapes.Ellipse{Width=4,Height=4,Fill=new SolidColorBrush(color.Value),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false});
            var number=new TextBlock{Text=value,FontSize=10,Foreground=primary,Margin=new Thickness(7,0,0,0),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(number,1);row.Children.Add(number);target.Children.Add(row);
        }
        private static DateTime Epoch(double milliseconds) { return new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddMilliseconds(milliseconds).ToLocalTime(); }
        private void OnRendering(object sender,EventArgs args) {
            var frame=args as RenderingEventArgs;if(frame!=null && frame.RenderingTime==lastRendering)return;if(frame!=null)lastRendering=frame.RenderingTime;
            if(IsVisible)follower.Tick(this,hwnd);
            if(follower.Dragging){scope.IsDropDownOpen=false;range.IsDropDownOpen=false;titlePopup.IsOpen=false;}
        }
        private void OnSourceInitialized(object sender,EventArgs e) {
            hwnd=new WindowInteropHelper(this).Handle;Native.SetWindowLongPtr(hwnd,-20,new IntPtr(Native.GetWindowLongPtr(hwnd,-20).ToInt64()|0x80));
            if(testMode)return;StartBackend();
            hookCallback=delegate(IntPtr h,uint evt,IntPtr window,int obj,int child,uint thread,uint time){if(window==follower.PetHandle && Native.IsWindowLifecycleEvent(evt,obj,child))Dispatcher.BeginInvoke(new Action(delegate{if(!follower.Dragging && !Native.IsWindowVisible(window))Hide();}));};
            hook=Native.SetWinEventHook(0x8000,0x800B,IntPtr.Zero,hookCallback,0,0,0);
            CompositionTarget.Rendering+=OnRendering;
            hiddenTimer.Tick+=delegate{if(!IsVisible)follower.Tick(this,hwnd);};hiddenTimer.Start();
        }
        private void StartBackend() {
            string node=Environment.GetEnvironmentVariable("CODEX_STATUS_NODE")??"node.exe";
            var start=new ProcessStartInfo(node,"\""+Path.Combine(root,"src","backend","main.mjs")+"\" --port 9337"){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
            backend=new Process{StartInfo=start,EnableRaisingEvents=true};
            backend.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e){
                if(String.IsNullOrEmpty(e.Data))return;
                try{var value=Json.Serializer.DeserializeObject(e.Data);
                    if(Json.Text(value,"type")=="view")Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(delegate{UpdateView(value);}));
                    else if(Json.Text(value,"type")=="pet")lock(inbox){queuedPet=value;if(!petQueued){petQueued=true;Dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(delegate{object latest;lock(inbox){latest=queuedPet;petQueued=false;}follower.Observe(latest);}));}}
                }catch{}
            };
            backend.ErrorDataReceived+=delegate{};
            backend.Exited+=delegate{if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(delegate{status.Text="数据进程已退出";}));};
            try{backend.Start();backend.BeginOutputReadLine();backend.BeginErrorReadLine();}catch{status.Text="数据进程启动失败";}
        }
        private void Send(object message) { if(testMode || backend==null)return;try{backend.StandardInput.WriteLine(Json.Serializer.Serialize(message));backend.StandardInput.Flush();}catch{} }
        public void RenderTo(string filename, bool showPreferences = false, bool showDetails = false) {
            var previousVisibility=preferences.Visibility;bool previousModels=modelsDisclosure.IsExpanded,previousAccounts=accountsDisclosure.IsExpanded;
            if(showPreferences)preferences.Visibility=Visibility.Visible;if(showDetails){modelsDisclosure.IsExpanded=true;accountsDisclosure.IsExpanded=true;}
            InvalidateMeasure();UpdateLayout();Dispatcher.Invoke(DispatcherPriority.Render,new Action(delegate{}));
            chart.FinishAnimation();Measure(new Size(Width,1000));Arrange(new Rect(0,0,Width,DesiredSize.Height));UpdateLayout();
            var target=new RenderTargetBitmap((int)Math.Ceiling(ActualWidth),(int)Math.Ceiling(ActualHeight),96,96,PixelFormats.Pbgra32);target.Render(this);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(target));using(var stream=File.Create(filename))png.Save(stream);
            preferences.Visibility=previousVisibility;modelsDisclosure.IsExpanded=previousModels;accountsDisclosure.IsExpanded=previousAccounts;
        }
    }

    internal static class Program {
        private static Mutex mutex;
        [STAThread] public static int Main(string[] args) {
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            bool test = args.Contains("--render") || args.Contains("--self-test");
            bool created; mutex = new Mutex(true, "Local\\CodexPetCredits" + (test ? "Test" : ""), out created); if (!created) return 0;
            try {
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                var window = new CompanionWindow(root, test); app.MainWindow = window;
                if (test) {
                    string output = args.Contains("--render") ? args[Array.IndexOf(args, "--render") + 1] : Path.Combine(root, "artifacts"); Directory.CreateDirectory(output);
                    var fixture = Json.Map(Json.Serializer.DeserializeObject(File.ReadAllText(Path.Combine(root, "tests", "view-fixture.json"))));
                    window.Show();
                    foreach (string theme in new[] { "dark", "light" }) for (int mode = 0; mode < 3; mode++) {
                        var settings = Json.Map(fixture["settings"]); settings["density"] = mode; settings["theme"] = theme; window.UpdateView(fixture);
                        window.Dispatcher.Invoke(DispatcherPriority.Render, new Action(delegate { }));
                        window.RenderTo(Path.Combine(output, theme + "-" + mode + ".png"));
                        if (window.ActualWidth < 300 || window.ActualHeight < 70 || window.ActualHeight > 900) throw new Exception("UI layout bounds failed");
                    }
                    foreach(string theme in new[]{"dark","light"})foreach(string accent in Palette.Keys){var settings=Json.Map(fixture["settings"]);settings["density"]=2;settings["theme"]=theme;settings["accent"]=accent;window.UpdateView(fixture);window.RenderTo(Path.Combine(output,theme+"-"+accent+".png"),true,true);}
                    window.Close(); File.WriteAllText(Path.Combine(output, "ui-test.txt"), "PASS: all three densities rendered in light and dark themes; layout bounds valid."); return 0;
                }
                // Create the handle without presenting a floating window before the pet is found.
                new WindowInteropHelper(window).EnsureHandle(); app.Run(); return 0;
            } catch (Exception e) { Directory.CreateDirectory(Path.Combine(root, "artifacts")); File.WriteAllText(Path.Combine(root, "artifacts", "startup-error.log"), e.ToString()); return 1; }
            finally { if (created) mutex.ReleaseMutex(); mutex.Dispose(); }
        }
    }
}
