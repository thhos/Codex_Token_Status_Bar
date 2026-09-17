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
    public sealed class CompanionWindow : Window {
        private readonly string root;
        private readonly bool testMode;
        private BackendClient backend;
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
        private UsageView state;
        private Dictionary<string, object> settings = new Dictionary<string, object>();
        private readonly Dictionary<string, object> pendingSettings = new Dictionary<string, object>();
        private readonly SettingsCommitter settingsCommitter;
        private readonly Border panel = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Padding = new Thickness(13) };
        private readonly StackPanel layout = new StackPanel(), trend = new StackPanel(), detail = new StackPanel(), preferences = new StackPanel();
        private readonly DockPanel header = new DockPanel { Margin = new Thickness(0,0,0,9) };
        private readonly AnimatedLayout headerReveal = new AnimatedLayout();
        private readonly AnimatedLayout contentReveal = new AnimatedLayout();
        private readonly DispatcherTimer headerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        private readonly StackPanel legendRows = new StackPanel(), modelRows = new StackPanel(), accountRows = new StackPanel();
        private readonly List<TextBlock> labels = new List<TextBlock>(), values = new List<TextBlock>();
        private readonly List<Border> cards = new List<Border>();
        private readonly List<Button> modeButtons = new List<Button>(), colorButtons = new List<Button>();
        private readonly ComboBox scope = new ComboBox(), range = new ComboBox();
        private readonly CreditChart chart = new CreditChart();
        private readonly Slider opacity = new Slider { Minimum = 40, Maximum = 100, Value = 90, Width = 145 };
        private readonly Popup titlePopup = new Popup { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom };
        private readonly Popup selectionPopup = new Popup { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom };
        private TextBlock amount, quotaLabel, forecast, forecastLabel, subTitle, total, totalLabel, intervalLabel, recentHour, recentDay, reset, status, coverage;
        private Button smoothButton, themeButton;
        private Button selectionButton, titleButton;
        private Expander modelsDisclosure, accountsDisclosure;
        private Brush primary, muted, accentBrush;

        public CompanionWindow(string projectRoot, bool testing) {
            root = projectRoot; testMode = testing;
            settingsCommitter=new SettingsCommitter(message=>Send(message));
            Title = "Codex Pet Credits"; Width = 320; Height=900; SizeToContent = SizeToContent.Manual;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
            Topmost = true; ShowInTaskbar = false; ShowActivated = false; FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 11;
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            // Keep the native layered window stable; only its painted panel changes height.
            var viewport=new Grid();viewport.Children.Add(panel);Content=viewport;panel.VerticalAlignment=VerticalAlignment.Bottom;
            headerReveal.Child=header;headerReveal.AnimateChanges=!testing;
            contentReveal.Child=new HeaderBodyPanel(headerReveal,layout);contentReveal.AnimateChanges=!testing;contentReveal.SuppressAnimation=()=>headerReveal.IsAnimating;
            panel.Child = new ScrollViewer { Content = contentReveal, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            BuildLayout(); ApplySettings();
            SetHeaderVisible(testing);
            MouseEnter+=delegate{headerTimer.Stop();SetHeaderVisible(true);};
            MouseLeave+=delegate{headerTimer.Start();};
            headerTimer.Tick+=delegate{
                if(IsMouseOver || scope.IsDropDownOpen || range.IsDropDownOpen || selectionPopup.IsOpen || titlePopup.IsOpen || (panel.ContextMenu!=null && panel.ContextMenu.IsOpen))return;
                headerTimer.Stop();SetHeaderVisible(false);
            };
            PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Tab){headerTimer.Stop();SetHeaderVisible(true);}};
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if(e.Key == Key.Escape) { titlePopup.IsOpen = false; preferences.Visibility = Visibility.Collapsed; ChangeSetting("density",0); } };
            SourceInitialized += OnSourceInitialized;
            Closed += delegate {
                settingsCommitter.Dispose();
                hiddenTimer.Stop();headerTimer.Stop(); CompositionTarget.Rendering -= OnRendering; titlePopup.IsOpen = false; selectionPopup.IsOpen = false;
                if(hook != IntPtr.Zero)Native.UnhookWinEvent(hook);
                if(backend!=null)backend.Dispose();
            };
        }
        private void SetHeaderVisible(bool visible){
            // Reveal the row progressively so content movement exactly matches panel growth.
            header.IsHitTestVisible=visible;header.Opacity=visible?1:0;
            headerReveal.Expanded=visible;
        }
        public double SurfaceHeight { get { return panel.ActualHeight; } }
        public double AttachmentStabilityMargin { get { return header.DesiredSize.Height+16; } }
        public double RequestedSurfaceHeight { get { return contentReveal.DesiredSize.Height+panel.Padding.Top+panel.Padding.Bottom+panel.BorderThickness.Top+panel.BorderThickness.Bottom; } }
        public double SurfaceOffset { get { return panel.TranslatePoint(new Point(),this).Y; } }
        public void SetSurfaceHeightLimit(double height){
            if(Math.Abs(panel.MaxHeight-height)>.01){panel.MaxHeight=height;UpdateLayout();}
        }
        public void SetAttachmentSide(int side){
            var alignment=side==0?VerticalAlignment.Bottom:VerticalAlignment.Top;
            if(panel.VerticalAlignment!=alignment){panel.VerticalAlignment=alignment;UpdateLayout();}
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
            var modes = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(modes,Dock.Right); header.Children.Add(modes);
            var icons = new[]{"−","∿","≡"};
            string[] paths={"M 1,6 L 12,6","M 1,10 L 4,5 L 7,8 L 12,2","M 2,2 L 12,2 M 2,6 L 12,6 M 2,10 L 9,10"};
            for(int i=0;i<3;i++){int mode=i;var button=ActionButton(icons[i],new[]{"极简","趋势","详细"}[i],delegate{ChangeSetting("density",density==mode?0:mode);});button.Content=new System.Windows.Shapes.Path{Data=Geometry.Parse(paths[i]),Stroke=Brushes.Gray,StrokeThickness=1.3,Width=12,Height=12,Stretch=Stretch.Uniform};modeButtons.Add(button);modes.Children.Add(button);}
            modes.Children.Add(ActionButton("⋯","外观设置",delegate{preferences.Visibility=preferences.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;}));
            header.Children.Add(Text("CODEX  /  用量",10));
            // Reserve more room for the forecast caption while keeping the companion compact.
            var summary = new Grid();foreach(double weight in new[]{1.0,1.8})summary.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(weight,GridUnitType.Star)});
            var quota = new StackPanel();amount=Text("—",24,true);amount.Height=36;quotaLabel=Text("周额度剩余",10);quota.Children.Add(amount);quota.Children.Add(quotaLabel);
            summary.Children.Add(Card(quota,new Thickness(0,0,4,0)));
            var prediction = new StackPanel();forecast=Text("学习中",24,true);forecast.Height=36;forecastLabel=Text("耗尽预测",10);prediction.Children.Add(forecast);prediction.Children.Add(forecastLabel);
            var predictionCard=Card(prediction,new Thickness(4,0,0,0));Grid.SetColumn(predictionCard,1);summary.Children.Add(predictionCard);layout.Children.Add(summary);
            layout.Children.Add(trend);trend.Margin=new Thickness(0,10,0,0);
            var filters = new StackPanel { VerticalAlignment=VerticalAlignment.Center, Margin=new Thickness(10,0,0,0) };
            ConfigureCombo(range,new[]{"1h","6h","24h","7d","30d"},104,"时间窗口");
            ConfigureCombo(scope,new[]{"当前任务","本机汇总","主要任务","主要项目"},104,"统计范围");
            scope.Height=range.Height=25;range.Margin=new Thickness(0,4,0,0);filters.Children.Add(scope);filters.Children.Add(range);
            scope.SelectionChanged+=delegate{if(!updating && scope.SelectedIndex>=0)ChangeSetting("scope",new[]{"current","account","tasks","projects"}[scope.SelectedIndex]);};
            range.SelectionChanged+=delegate{if(!updating && range.SelectedItem!=null)ChangeSetting("range",range.SelectedItem.ToString());};
            subTitle=Text("正在读取任务",10);subTitle.MaxHeight=18;
            titleButton=ActionButton("","查看完整名称",delegate{ShowTitle(subTitle.Tag as string ?? subTitle.Text);},Double.NaN);
            titleButton.Content=subTitle;titleButton.HorizontalContentAlignment=HorizontalAlignment.Left;titleButton.Height=23;titleButton.Padding=new Thickness(0);titleButton.Margin=new Thickness(0,3,0,3);titleButton.BorderThickness=new Thickness(0);
            titlePopup.PlacementTarget=titleButton;
            var totalStack=new StackPanel { VerticalAlignment=VerticalAlignment.Center };total=Text("— cr",25,true);totalLabel=Text("时段消耗",10);totalStack.Children.Add(total);totalStack.Children.Add(totalLabel);
            var totalGrid=new Grid();totalGrid.ColumnDefinitions.Add(new ColumnDefinition());totalGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});totalGrid.Children.Add(totalStack);Grid.SetColumn(filters,1);totalGrid.Children.Add(filters);
            trend.Children.Add(Card(totalGrid,new Thickness(0,0,0,4)));trend.Children.Add(titleButton);
            selectionButton=ActionButton("选择任务","选择要比较的任务或项目",ShowSelection,Double.NaN);selectionButton.HorizontalAlignment=HorizontalAlignment.Left;selectionButton.Margin=new Thickness(0,0,0,5);selectionButton.Visibility=Visibility.Collapsed;trend.Children.Add(selectionButton);selectionPopup.PlacementTarget=selectionButton;
            var chartHeader=new DockPanel();
            smoothButton=ActionButton("平滑","切换平滑趋势或区间原值",delegate{ChangeSetting("smoothing",Json.Text(settings,"smoothing","smooth")=="smooth"?"raw":"smooth");},48);
            smoothButton.Height=23;DockPanel.SetDock(smoothButton,Dock.Right);chartHeader.Children.Add(smoothButton);
            intervalLabel=Text("消耗趋势",10);chartHeader.Children.Add(intervalLabel);trend.Children.Add(chartHeader);
            chart.Height=108;trend.Children.Add(chart);legendRows.Margin=new Thickness(0,3,0,0);trend.Children.Add(legendRows);
            layout.Children.Add(detail);detail.Margin=new Thickness(0,10,0,0);
            var recent = new Grid();recent.ColumnDefinitions.Add(new ColumnDefinition());recent.ColumnDefinitions.Add(new ColumnDefinition());
            var hourStack=new StackPanel();recentHour=Text("—",19,true);hourStack.Children.Add(recentHour);hourStack.Children.Add(Text("本机近 1h",10));
            recent.Children.Add(Card(hourStack,new Thickness(0,0,4,0)));
            var dayStack=new StackPanel();recentDay=Text("—",19,true);dayStack.Children.Add(recentDay);dayStack.Children.Add(Text("本机近 24h",10));
            var dayCard=Card(dayStack,new Thickness(4,0,0,0));Grid.SetColumn(dayCard,1);recent.Children.Add(dayCard);detail.Children.Add(recent);
            modelsDisclosure=Disclosure("模型使用量",modelRows);detail.Children.Add(modelsDisclosure);
            accountsDisclosure=Disclosure("其他额度",accountRows);detail.Children.Add(accountsDisclosure);
            reset=Text("",10);reset.Margin=new Thickness(0,9,0,0);detail.Children.Add(reset);
            status=Text("正在连接",10);
            preferences.Visibility=Visibility.Collapsed;preferences.Margin=new Thickness(0,12,0,0);layout.Children.Add(preferences);
            preferences.Children.Add(Text("主题色",10));var swatches=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,6,0,10)};
            for(int i=0;i<Palette.Keys.Length;i++){int index=i;var button=ActionButton("",Palette.Names[i],delegate{ChangeSetting("accent",Palette.Keys[index]);},40);colorButtons.Add(button);swatches.Children.Add(button);}
            preferences.Children.Add(swatches);
            var appearance=new DockPanel();themeButton=ActionButton("深色","切换浅色或深色",delegate{ChangeSetting("theme",dark?"light":"dark");},54);DockPanel.SetDock(themeButton,Dock.Right);appearance.Children.Add(themeButton);appearance.Children.Add(Text("背景不透明度",10));appearance.Children.Add(opacity);preferences.Children.Add(appearance);
            AutomationProperties.SetName(opacity,"背景不透明度");
            opacity.ValueChanged+=delegate{if(!updating)ChangeSetting("opacity",opacity.Value);};
            opacity.AddHandler(Thumb.DragCompletedEvent,new DragCompletedEventHandler(delegate{settingsCommitter.Flush();}));
            coverage=Text("",10);coverage.TextWrapping=TextWrapping.Wrap;coverage.TextTrimming=TextTrimming.None;coverage.MaxHeight=170;
            preferences.Children.Add(Disclosure("数据与预测说明",new ScrollViewer{Content=coverage,MaxHeight=170,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}));
            var menu=new ContextMenu();
            foreach(var entry in new[]{"极简","趋势","详细","刷新额度","退出挂件"}){string action=entry;var item=new MenuItem{Header=entry};item.Click+=delegate{int mode=Array.IndexOf(new[]{"极简","趋势","详细"},action);if(mode>=0)ChangeSetting("density",mode);else if(action=="刷新额度")Send(new Dictionary<string,object>{{"type","refresh"}});else Close();};menu.Items.Add(item);}panel.ContextMenu=menu;
        }
        private void ConfigureCombo(ComboBox combo,string[] items,double width,string name) {
            foreach(var item in items)combo.Items.Add(item);
            combo.Width=width;combo.Height=27;combo.FontSize=11;combo.HorizontalAlignment=HorizontalAlignment.Left;
            AutomationProperties.SetName(combo,name);
            combo.Template=(ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ComboBox'><Grid><ToggleButton Focusable='False' IsChecked='{Binding IsDropDownOpen,Mode=TwoWay,RelativeSource={RelativeSource TemplatedParent}}' ClickMode='Press' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'><ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='6'><Path Data='M 0,0 L 4,4 L 8,0' Stroke='#87949F' StrokeThickness='1.2' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,8,0'/></Border></ControlTemplate></ToggleButton.Template></ToggleButton><ContentPresenter Content='{TemplateBinding SelectionBoxItem}' IsHitTestVisible='False' Margin='9,0,23,0' VerticalAlignment='Center'/><Popup x:Name='PART_Popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}' AllowsTransparency='True' Focusable='False'><Border Background='{DynamicResource DropdownSurface}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' Padding='3' CornerRadius='6' MinWidth='{TemplateBinding ActualWidth}'><ScrollViewer CanContentScroll='True'><ItemsPresenter x:Name='ItemsPresenter'/></ScrollViewer></Border></Popup></Grid><ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter Property='BorderBrush' Value='#879AA8'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
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
        // Work on a draft so checking several rows never rebuilds or closes the popup mid-selection.
        private void ShowSelection() {
            if(selectionPopup.IsOpen){selectionPopup.IsOpen=false;return;}
            string key=Json.Text(settings,"scope")=="projects"?"selectedProjects":"selectedTasks";
            var choices=state==null?new ComparisonChoice[0]:state.Choices;
            var draft=Json.Get(settings,key)==null?new HashSet<string>(choices.Where(c=>c.Selected).Select(c=>c.Id)):new HashSet<string>(Json.Items(Json.Get(settings,key)).Select(Convert.ToString));
            var content=new StackPanel();var count=new TextBlock{Foreground=muted,FontSize=11,Margin=new Thickness(0,0,0,8)};content.Children.Add(count);
            var search=new TextBox{FontSize=12,Padding=new Thickness(6),Margin=new Thickness(0,0,0,6),Foreground=primary,Background=panel.Background};AutomationProperties.SetName(search,"搜索任务或项目");content.Children.Add(search);
            var rows=new StackPanel();content.Children.Add(new ScrollViewer{Content=rows,MaxHeight=250,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
            var checks=new List<CheckBox>();Action updateCount=delegate{count.Text="最多 5 项 · 已选 "+draft.Count;foreach(var box in checks)box.IsEnabled=box.IsChecked==true || draft.Count<5;};
            Action populate=delegate{
                rows.Children.Clear();checks.Clear();
                foreach(var choice in choices){string id=choice.Id,name=choice.Name;if((name+id).IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)<0)continue;
                    var box=new CheckBox{Tag=id,Content=new TextBlock{Text=name,MaxWidth=265,TextTrimming=TextTrimming.CharacterEllipsis,Foreground=primary},IsChecked=draft.Contains(id),Margin=new Thickness(1,6,1,6),ToolTip=UiText.Short(name,60)};
                    box.Checked+=delegate{draft.Add(id);updateCount();};box.Unchecked+=delegate{draft.Remove(id);updateCount();};rows.Children.Add(box);checks.Add(box);
                }
                if(checks.Count==0)rows.Children.Add(new TextBlock{Text="没有匹配项",Foreground=muted,Margin=new Thickness(0,8,0,8)});updateCount();
            };
            search.TextChanged+=delegate{populate();};populate();
            var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0)};
            var automatic=ActionButton("自动前三","恢复自动选择",delegate{selectionPopup.IsOpen=false;ChangeSetting(key,null);},80);
            var apply=ActionButton("应用","应用所选任务或项目",delegate{selectionPopup.IsOpen=false;ChangeSetting(key,choices.Select(c=>c.Id).Where(draft.Contains).ToArray());},62);
            automatic.Foreground=muted;apply.Foreground=accentBrush;apply.BorderBrush=accentBrush;actions.Children.Add(automatic);actions.Children.Add(apply);content.Children.Add(actions);
            selectionPopup.Child=new Border{Child=content,Width=330,Padding=new Thickness(12),CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1),BorderBrush=panel.BorderBrush,Background=new SolidColorBrush(dark?Color.FromRgb(32,35,43):Color.FromRgb(247,247,250))};
            selectionPopup.IsOpen=true;
        }
        private void ChangeSetting(string key,object value) {
            settings[key]=value;if(!testMode)pendingSettings[key]=value;
            if(key=="opacity"){
                updating=true;opacity.Value=Convert.ToDouble(value);updating=false;
                ApplyOpacity();settingsCommitter.Set(key,value);return;
            }
            ApplySettings();UpdateContent();Send(new Dictionary<string,object>{{"type","settings"},{key,value}});
        }
        private void ApplyOpacity(){
            // Mutate only the translucent surfaces; keep controls, chart and theme resources intact.
            byte alpha=(byte)Math.Round(opacity.Value*2.55);
            SetAlpha(panel.Background,alpha);
            foreach(var combo in new[]{scope,range}){
                SetAlpha(combo.Background,(byte)Math.Round(26*opacity.Value/100));
                SetAlpha(combo.Resources["DropdownSurface"] as Brush,alpha);
            }
        }
        private static void SetAlpha(Brush brush,byte alpha){
            var solid=brush as SolidColorBrush;if(solid==null || solid.IsFrozen)return;
            var color=solid.Color;if(color.A==alpha)return;color.A=alpha;solid.Color=color;
        }
        private void ApplySettings() {
            updating=true;density=(int)Json.Number(settings,"density");dark=Json.Text(settings,"theme","dark")!="light";accent=Json.Text(settings,"accent","mint");
            Width=368;trend.Visibility=density>=1?Visibility.Visible:Visibility.Collapsed;detail.Visibility=density==2?Visibility.Visible:Visibility.Collapsed;
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
            smoothButton.Foreground=muted;smoothButton.BorderBrush=panel.BorderBrush;themeButton.Foreground=primary;themeButton.BorderBrush=panel.BorderBrush;selectionButton.Foreground=muted;selectionButton.BorderBrush=panel.BorderBrush;
            for(int i=0;i<colorButtons.Count;i++){var button=colorButtons[i];button.Content=new System.Windows.Shapes.Ellipse{Width=13,Height=13,Fill=new SolidColorBrush(Palette.Accent(Palette.Keys[i],dark))};button.BorderBrush=Palette.Keys[i]==accent?accentBrush:Brushes.Transparent;}
            foreach(var combo in new[]{scope,range}){
                combo.Foreground=primary;combo.Background=new SolidColorBrush(Color.FromArgb((byte)Math.Round(26*opacity.Value/100),tint.R,tint.G,tint.B));combo.BorderBrush=panel.BorderBrush;
                // Dropdown surfaces need their own background because they live in separate native windows.
                combo.Resources["DropdownSurface"]=new SolidColorBrush(dark?Color.FromArgb(alpha,32,35,43):Color.FromArgb(alpha,247,247,250));
                var itemStyle=new Style(typeof(ComboBoxItem));itemStyle.Setters.Add(new Setter(Control.ForegroundProperty,primary));itemStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(8,7,8,7)));combo.ItemContainerStyle=itemStyle;
            }
            chart.Dark=dark;chart.Accent=accent;chart.InvalidateVisual();
        }
        public void UpdateView(object data) {
            state=new UsageView(data);ApplyReceivedSettings(state.Settings);
            UpdateContent();
        }
        private void ApplyReceivedSettings(Dictionary<string,object> incoming){
            var received=new Dictionary<string,object>(incoming);
            // A delayed view must not undo a menu choice while its settings acknowledgement is in flight.
            foreach(var entry in pendingSettings.ToArray()){if(Json.Serializer.Serialize(Json.Get(received,entry.Key))==Json.Serializer.Serialize(entry.Value))pendingSettings.Remove(entry.Key);else received[entry.Key]=entry.Value;}
            string oldSettings=Json.Serializer.Serialize(settings);settings=received;
            if(oldSettings!=Json.Serializer.Serialize(settings))ApplySettings();
        }
        private static string Credit(double? value){return UsageView.Credits(value);}
        private void UpdateText(TextBlock element,string value) { TextTransition.Set(element,value,!testMode); }
        private void UpdateContent() {
            if(state==null)return;
            UpdateText(amount,state.Remaining.HasValue?state.Remaining.Value.ToString("0")+"%":"—");
            quotaLabel.Text=state.QuotaLabel;
            UpdateText(forecast,state.ForecastValue);
            string predictionLabel=state.ForecastLabel;
            bool earlyDepletion=predictionLabel=="！预计提前耗尽";
            // Coupons are relevant when the forecast warns of running out before reset.
            string couponLabel=!state.ResetCredits.HasValue?"券数未知":state.ResetCredits.Value==0?"无重置券":state.ResetCredits.Value.ToString("0")+" 张重置券";
            UpdateText(forecastLabel,predictionLabel+(earlyDepletion?" · "+couponLabel:""));
            // The caution color is semantic and independent of the selected accent color.
            forecastLabel.Foreground=earlyDepletion?new SolidColorBrush(dark?Color.FromRgb(246,205,97):Color.FromRgb(139,100,0)):muted;
            forecastLabel.ToolTip=state.Warning?"预计在重置前耗尽":"结合工作习惯估计";
            forecast.ToolTip="结合工作习惯与近期速度";
            string title=Json.Text(settings,"scope","current")=="current"?state.TaskTitle:state.Subtitle;
            subTitle.Tag=title;subTitle.Text=UiText.Short(title,30);
            string scopeKey=Json.Text(settings,"scope","current");bool comparison=scopeKey=="tasks" || scopeKey=="projects";
            titleButton.Visibility=scopeKey=="current"?Visibility.Visible:Visibility.Collapsed;selectionButton.Visibility=comparison?Visibility.Visible:Visibility.Collapsed;
            selectionButton.Content="选择"+(scopeKey=="projects"?"项目":"任务")+" · "+state.Series.Length+" 项";
            UpdateText(total,Credit(state.Total)+" cr");
            totalLabel.Text=comparison?"已选合计":"时段消耗";
            totalLabel.ToolTip="图中曲线在所选时段内的消耗合计";
            var series=state.Series;
            intervalLabel.Text="消耗趋势";
            chart.Names=series.Select(s=>s.Name).ToArray();
            chart.Smooth=Json.Text(settings,"smoothing","smooth")=="smooth";
            chart.ContextKey=state.ChartKey+":"+chart.Smooth;
            chart.Start=state.Start;chart.End=state.End;
            chart.ObservedAt=state.ObservedAt;
            chart.SetSeries(series.Select(s=>s.Points).ToList());
            UpdateText(recentHour,Credit(state.RecentHour));UpdateText(recentDay,Credit(state.RecentDay));
            UpdateText(reset,"额度重置  "+state.ResetLabel);
            UpdateText(coverage,state.Coverage+"\n\n"+state.ForecastDetail+"\n\n周期越长，平滑强度越低。虚线仅连接数据不完整的区间，不补算消耗；悬停显示区间原值，合计保持原始估算。\n费率 "+state.RateVersion);
            string signature=Json.Serializer.Serialize(new object[]{series.Select(s=>new[]{s.Name,Credit(s.Total)}),state.Models,state.OtherQuotas,state.OfficialCredits,dark,accent});
            if(signature==detailSignature)return;detailSignature=signature;
            var legends=new List<RowValue>();
            if(series.Length>1)for(int i=0;i<Math.Min(5,series.Length);i++)legends.Add(new RowValue(series[i].Name,Credit(series[i].Total)+" cr",Palette.Series(accent,dark,i)));
            UpdateRows(legendRows,legends);
            var models=state.Models.Take(5).Select(m=>new RowValue(m.Name,m.Value)).ToList();
            if(models.Count==0)models.Add(new RowValue("暂无模型记录","—"));UpdateRows(modelRows,models);
            var accounts=state.OtherQuotas.Select(r=>new RowValue(r.Name,r.Value)).ToList();
            if(state.OfficialCredits!=null)accounts.Add(new RowValue("当前任务 · 服务端累计",Credit(state.OfficialCredits)+" cr"));
            UpdateRows(accountRows,accounts);
            accountsDisclosure.Visibility=accountRows.Children.Count==0?Visibility.Collapsed:Visibility.Visible;
        }
        private sealed class RowValue {
            public readonly string Title,Value;public readonly Color? Color;
            public RowValue(string title,string value,Color? color=null){Title=title;Value=value;Color=color;}
        }
        private void UpdateRows(StackPanel target,List<RowValue> values) {
            // Retain matching visual rows so changed numbers can fade without rebuilding the panel.
            for(int i=0;i<values.Count;i++) {
                var value=values[i];var row=i<target.Children.Count?target.Children[i] as Grid:null;
                if(row==null || Convert.ToString(row.Tag)!=value.Title) {
                    if(row!=null)target.Children.RemoveAt(i);
                    AddRow(target,value.Title,value.Value,value.Color);
                    row=(Grid)target.Children[target.Children.Count-1];target.Children.Remove(row);target.Children.Insert(i,row);
                } else {
                    var button=(Button)row.Children[0];((TextBlock)button.Content).Foreground=muted;
                    var number=row.Children.OfType<TextBlock>().Single();number.Foreground=primary;UpdateText(number,value.Value);
                    var dot=row.Children.OfType<System.Windows.Shapes.Ellipse>().FirstOrDefault();if(dot!=null && value.Color.HasValue)dot.Fill=new SolidColorBrush(value.Color.Value);
                }
            }
            while(target.Children.Count>values.Count)target.Children.RemoveAt(target.Children.Count-1);
        }
        private void AddRow(StackPanel target,string title,string value,Color? color) {
            var row=new Grid{Tag=title,Margin=new Thickness(0,4,0,0)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var name=new TextBlock{Text=UiText.Short(title,26),FontSize=10,Foreground=muted,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};
            var titleButton=ActionButton("","查看完整名称",delegate{ShowTitle(title);},Double.NaN);titleButton.Content=name;titleButton.Height=19;titleButton.Padding=new Thickness(color.HasValue?12:0,0,0,0);titleButton.HorizontalContentAlignment=HorizontalAlignment.Left;titleButton.BorderThickness=new Thickness(0);row.Children.Add(titleButton);
            if(color.HasValue)row.Children.Add(new System.Windows.Shapes.Ellipse{Width=4,Height=4,Fill=new SolidColorBrush(color.Value),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false});
            var number=new TextBlock{Text=value,FontSize=10,Foreground=primary,Margin=new Thickness(7,0,0,0),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(number,1);row.Children.Add(number);target.Children.Add(row);
        }
        private void OnRendering(object sender,EventArgs args) {
            var frame=args as RenderingEventArgs;if(frame!=null && frame.RenderingTime==lastRendering)return;if(frame!=null)lastRendering=frame.RenderingTime;
            if(IsVisible)follower.Tick(this,hwnd);
            if(follower.Dragging){scope.IsDropDownOpen=false;range.IsDropDownOpen=false;titlePopup.IsOpen=false;selectionPopup.IsOpen=false;}
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
            backend=new BackendClient();
            backend.ViewReceived+=delegate(object value){
                // Keep views and settings acknowledgements in wire order; only geometry is coalesced.
                Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(delegate{UpdateView(value);}));
            };
            backend.PetReceived+=delegate(object value){
                lock(inbox){queuedPet=value;if(petQueued)return;petQueued=true;}
                Dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(delegate{
                    object latest;lock(inbox){latest=queuedPet;petQueued=false;}follower.Observe(latest);
                }));
            };
            backend.SettingsReceived+=delegate(object value){
                Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(delegate{ApplyReceivedSettings(Json.Map(Json.Get(value,"settings")));}));
            };
            backend.Failed+=delegate(string message){if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(delegate{status.Text=message;}));};
            backend.Start(root);
        }
        private void Send(object message){if(!testMode && backend!=null)backend.Send(message);}
        public void RenderTo(string filename, bool showPreferences = false, bool showDetails = false, int selectedBucket = -1, bool showHeader = true) {
            var previousVisibility=preferences.Visibility;bool previousModels=modelsDisclosure.IsExpanded,previousAccounts=accountsDisclosure.IsExpanded;
            double previousHeaderOpacity=header.Opacity;bool previousHeaderExpanded=headerReveal.Expanded;header.Opacity=showHeader?1:0;headerReveal.Expanded=showHeader;chart.SelectBucket(selectedBucket);
            if(showPreferences)preferences.Visibility=Visibility.Visible;if(showDetails){modelsDisclosure.IsExpanded=true;accountsDisclosure.IsExpanded=true;}
            InvalidateMeasure();UpdateLayout();Dispatcher.Invoke(DispatcherPriority.Render,new Action(delegate{}));
            chart.FinishAnimation();UpdateLayout();
            var target=new RenderTargetBitmap((int)Math.Ceiling(panel.ActualWidth),(int)Math.Ceiling(panel.ActualHeight),96,96,PixelFormats.Pbgra32);
            var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawRectangle(new VisualBrush(panel),null,new Rect(0,0,panel.ActualWidth,panel.ActualHeight));target.Render(visual);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(target));using(var stream=File.Create(filename))png.Save(stream);
            preferences.Visibility=previousVisibility;modelsDisclosure.IsExpanded=previousModels;accountsDisclosure.IsExpanded=previousAccounts;
            header.Opacity=previousHeaderOpacity;headerReveal.Expanded=previousHeaderExpanded;chart.SelectBucket(-1);
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
                        if (window.ActualWidth < 300 || window.SurfaceHeight < 70 || window.SurfaceHeight > 900) throw new Exception("UI layout bounds failed");
                    }
                    foreach(string theme in new[]{"dark","light"})foreach(string accent in Palette.Keys){var settings=Json.Map(fixture["settings"]);settings["density"]=2;settings["theme"]=theme;settings["accent"]=accent;window.UpdateView(fixture);window.RenderTo(Path.Combine(output,theme+"-"+accent+".png"),true,true);}
                    // Exercise bounded gaps in both themes using explicitly synthetic preview data.
                    foreach(var series in Json.Items(fixture["series"])){
                        var points=Json.Items(Json.Get(series,"points")).ToArray();for(int i=12;i<18 && i<points.Length;i++)points[i]=null;
                        Json.Map(series)["points"]=points;
                    }
                    fixture["windowStart"]=Json.Number(fixture,"windowEnd")-TimeSpan.FromDays(30).TotalMilliseconds;
                    foreach(string theme in new[]{"dark","light"}){var settings=Json.Map(fixture["settings"]);settings["density"]=1;settings["theme"]=theme;settings["range"]="30d";settings["accent"]="blue";settings["smoothing"]="smooth";window.UpdateView(fixture);window.RenderTo(Path.Combine(output,theme+"-gaps.png"));}
                    foreach(string theme in new[]{"dark","light"}){Json.Map(fixture["settings"])["theme"]=theme;window.UpdateView(fixture);window.RenderTo(Path.Combine(output,theme+"-readout.png"),false,false,8);}
                    Json.Map(fixture["settings"])["theme"]="dark";window.UpdateView(fixture);window.RenderTo(Path.Combine(output,"dark-idle.png"),false,false,-1,false);
                    window.Close(); File.WriteAllText(Path.Combine(output, "ui-test.txt"), "PASS: all three densities rendered in light and dark themes; layout bounds valid."); return 0;
                }
                // Create the handle without presenting a floating window before the pet is found.
                new WindowInteropHelper(window).EnsureHandle(); app.Run(); return 0;
            } catch (Exception e) { Directory.CreateDirectory(Path.Combine(root, "artifacts")); File.WriteAllText(Path.Combine(root, "artifacts", "startup-error.log"), e.ToString()); return 1; }
            finally { if (created) mutex.ReleaseMutex(); mutex.Dispose(); }
        }
    }
}
