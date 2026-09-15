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
        private IntPtr hwnd, petHandle, hook;
        private Native.WinEventProc hookCallback;
        private readonly DispatcherTimer followTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        private DateTime lastDiscover = DateTime.MinValue, lastFrame = DateTime.UtcNow, missingSince = DateTime.MinValue;
        private bool hadCodex, firstPosition = true, updating, dark = true;
        private int density;
        private double backgroundOpacity = 90, physicalX, physicalY;
        private object state;
        private readonly Border panel = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Padding = new Thickness(16, 12, 16, 12) };
        private readonly StackPanel layout = new StackPanel();
        private readonly StackPanel trend = new StackPanel(), detail = new StackPanel();
        private readonly TextBlock quotaLabel, amount, forecast, subTitle, total, legend, status, coverage, reset, other;
        private readonly Button alert;
        private readonly CreditChart chart = new CreditChart();
        private readonly ComboBox range = new ComboBox(), scope = new ComboBox();
        private readonly StackPanel detailRows = new StackPanel();
        private readonly Slider opacity = new Slider { Minimum = 40, Maximum = 100, Value = 90, Width = 130, TickFrequency = 5, IsSnapToTickEnabled = false, Margin = new Thickness(8, 0, 0, 0) };
        private readonly List<TextBlock> textBlocks = new List<TextBlock>();
        private readonly List<Button> buttons = new List<Button>();
        public CompanionWindow(string projectRoot, bool testing) {
            root = projectRoot; testMode = testing;
            Title = "Codex Pet Credits"; Width = 310; SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
            Topmost = true; ShowInTaskbar = false; ShowActivated = false; FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 12;
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            Content = panel; panel.Child = new ScrollViewer { Content = layout, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var header = new DockPanel { LastChildFill = true };
            var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            controls.Children.Add(Button("−", "极简", delegate { SetDensity(0); }));
            controls.Children.Add(Button("∿", "趋势", delegate { SetDensity(density == 1 ? 0 : 1); }));
            controls.Children.Add(Button("≡", "详细", delegate { SetDensity(density == 2 ? 0 : 2); }));
            DockPanel.SetDock(controls, Dock.Right); header.Children.Add(controls);
            quotaLabel = Label("CODEX · 周剩余", 10); quotaLabel.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(quotaLabel); layout.Children.Add(header);
            var primary = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            alert = Button("!", "查看额度预测说明", delegate { MessageBox.Show(Json.Text(state, "forecastDetail", "正在学习工作习惯"), "消耗预测", MessageBoxButton.OK, MessageBoxImage.Information); });
            alert.Visibility = Visibility.Collapsed; DockPanel.SetDock(alert, Dock.Right); primary.Children.Add(alert);
            amount = Label("—", 28); amount.FontWeight = FontWeights.SemiBold; primary.Children.Add(amount); layout.Children.Add(primary);
            forecast = Label("正在学习", 11); forecast.Margin = new Thickness(0, 1, 0, 1); layout.Children.Add(forecast);
            trend.Margin = new Thickness(0, 14, 0, 0); layout.Children.Add(trend);
            subTitle = Label("本机记录", 11); subTitle.TextTrimming = TextTrimming.CharacterEllipsis; trend.Children.Add(subTitle);
            var filters = new DockPanel { Margin = new Thickness(0, 10, 0, 8) };
            foreach (var item in new[] { "当前任务", "本机汇总", "主要任务", "主要项目" }) scope.Items.Add(item);
            foreach (var item in new[] { "1h", "6h", "24h", "7d", "30d" }) range.Items.Add(item);
            scope.SelectedIndex = 0; range.SelectedIndex = 2; scope.Width = 108; range.Width = 70; scope.FontSize = range.FontSize = 11; scope.HorizontalAlignment = HorizontalAlignment.Left;
            scope.Height = range.Height = 29;
            string comboTemplate = @"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ComboBox'>
              <Grid><ToggleButton Focusable='False' ClickMode='Press' IsChecked='{Binding IsDropDownOpen,Mode=TwoWay,RelativeSource={RelativeSource TemplatedParent}}' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'>
                <ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='6'><Path Data='M 0 0 L 4 4 L 8 0' Stroke='#8ba1af' StrokeThickness='1.5' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,9,0'/></Border></ControlTemplate></ToggleButton.Template>
              </ToggleButton><ContentPresenter IsHitTestVisible='False' Content='{TemplateBinding SelectionBoxItem}' VerticalAlignment='Center' Margin='9,0,24,0'/>
              <Popup x:Name='PART_Popup' IsOpen='{TemplateBinding IsDropDownOpen}' Placement='Bottom' AllowsTransparency='True' Focusable='False'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='6' MinWidth='{TemplateBinding ActualWidth}' Padding='3'><ScrollViewer><ItemsPresenter/></ScrollViewer></Border></Popup>
              </Grid><ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter Property='BorderBrush' Value='#4dd4b0'/></Trigger></ControlTemplate.Triggers></ControlTemplate>";
            scope.Template = (ControlTemplate)XamlReader.Parse(comboTemplate); range.Template = (ControlTemplate)XamlReader.Parse(comboTemplate);
            range.HorizontalAlignment = HorizontalAlignment.Right; DockPanel.SetDock(range, Dock.Right); filters.Children.Add(range); filters.Children.Add(scope); trend.Children.Add(filters);
            AutomationProperties.SetName(scope, "统计范围"); AutomationProperties.SetName(range, "统计窗口长度");
            scope.SelectionChanged += delegate { if (!updating) SendSetting("scope", new[] { "current", "account", "tasks", "projects" }[Math.Max(0, scope.SelectedIndex)]); };
            range.SelectionChanged += delegate { if (!updating && range.SelectedItem != null) SendSetting("range", range.SelectedItem.ToString()); };
            total = Label("— cr", 21); total.FontWeight = FontWeights.SemiBold; trend.Children.Add(total);
            var unit = Label("此窗口消耗 · 估算 credits", 10); unit.Margin = new Thickness(0, 0, 0, 8); trend.Children.Add(unit);
            trend.Children.Add(chart); legend = Label("", 10); legend.TextWrapping = TextWrapping.Wrap; legend.Margin = new Thickness(0, 7, 0, 0); trend.Children.Add(legend);
            detail.Margin = new Thickness(0, 13, 0, 0); layout.Children.Add(detail);
            detail.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(54, 69, 80)), Margin = new Thickness(0, 0, 0, 12) });
            reset = Label("", 11); detail.Children.Add(reset); detail.Children.Add(detailRows);
            other = Label("", 10); other.TextWrapping = TextWrapping.Wrap; other.Margin = new Thickness(0, 8, 0, 8); detail.Children.Add(other);
            var settingsRow = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
            settingsRow.Children.Add(Label("背景不透明度", 11)); settingsRow.Children.Add(opacity);
            var themeButton = Button("◐", "切换浅色或深色", delegate { dark = !dark; ApplyTheme(); SendSetting("theme", dark ? "dark" : "light"); });
            DockPanel.SetDock(themeButton, Dock.Right); settingsRow.Children.Add(themeButton); detail.Children.Add(settingsRow);
            AutomationProperties.SetName(opacity, "面板背景不透明度，40 至 100 百分比");
            opacity.ValueChanged += delegate { backgroundOpacity = opacity.Value; ApplyTheme(); if (!updating) SendSetting("opacity", opacity.Value); };
            coverage = Label("", 10); coverage.TextWrapping = TextWrapping.Wrap; coverage.Margin = new Thickness(0, 6, 0, 0); detail.Children.Add(coverage);
            status = Label("正在连接…", 10); status.TextWrapping = TextWrapping.Wrap; status.Margin = new Thickness(0, 8, 0, 0); detail.Children.Add(status);
            var menu = new ContextMenu();
            AddMenu(menu, "极简", delegate { SetDensity(0); }); AddMenu(menu, "趋势", delegate { SetDensity(1); }); AddMenu(menu, "详细 / 透明度", delegate { SetDensity(2); });
            AddMenu(menu, "刷新额度", delegate { Send(new Dictionary<string, object> { { "type", "refresh" } }); });
            AddMenu(menu, "退出挂件", delegate { Close(); }); panel.ContextMenu = menu;
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) SetDensity(0); };
            SourceInitialized += OnSourceInitialized;
            Closed += delegate { followTimer.Stop(); if (hook != IntPtr.Zero) Native.UnhookWinEvent(hook); Send(new Dictionary<string, object> { { "type", "shutdown" } }); if (backend != null) { try { backend.StandardInput.Close(); } catch { } } };
            SetDensity(0, false); ApplyTheme();
        }
        private TextBlock Label(string text, double size) { var label = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center }; textBlocks.Add(label); return label; }
        private Button Button(string text, string hint, Action action) {
            var button = new Button { Content = text, ToolTip = hint, Width = 27, Height = 25, FontSize = 15, BorderThickness = new Thickness(0), Margin = new Thickness(2, 0, 0, 0), Cursor = Cursors.Hand, Padding = new Thickness(0) };
            button.Template = (ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border CornerRadius='6' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Opacity' Value='0.8'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter Property='BorderBrush' Value='#4dd4b0'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
            AutomationProperties.SetName(button, hint); button.Click += delegate { action(); }; buttons.Add(button); return button;
        }
        private static void AddMenu(ContextMenu menu, string label, Action action) { var item = new MenuItem { Header = label }; item.Click += delegate { action(); }; menu.Items.Add(item); }
        private void SetDensity(int value, bool save = true) {
            int previousDensity = density;
            density = value; Width = value == 0 ? 310 : value == 1 ? 360 : 420;
            trend.Visibility = value > 0 ? Visibility.Visible : Visibility.Collapsed; detail.Visibility = value > 1 ? Visibility.Visible : Visibility.Collapsed;
            if (save) SendSetting("density", value); firstPosition = true;
            if (value > previousDensity && SystemParameters.ClientAreaAnimation && !testMode) {
                var content = value == 1 ? trend : detail; content.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            }
        }
        private void ApplyTheme() {
            byte alpha = (byte)Math.Round(backgroundOpacity * 2.55);
            panel.Background = new SolidColorBrush(dark ? Color.FromArgb(alpha, 20, 30, 39) : Color.FromArgb(alpha, 246, 249, 251));
            panel.BorderBrush = new SolidColorBrush(dark ? Color.FromRgb(63, 83, 96) : Color.FromRgb(184, 202, 213));
            var primary = new SolidColorBrush(dark ? Color.FromRgb(233, 241, 244) : Color.FromRgb(23, 42, 56));
            foreach (var text in textBlocks) text.Foreground = primary;
            foreach (var button in buttons) { button.Foreground = primary; button.Background = new SolidColorBrush(dark ? Color.FromRgb(41, 56, 67) : Color.FromRgb(221, 232, 237)); }
            foreach (var combo in new[] { scope, range }) {
                combo.Foreground = primary; combo.Background = new SolidColorBrush(dark ? Color.FromRgb(31, 46, 57) : Color.FromRgb(233, 240, 244)); combo.BorderBrush = panel.BorderBrush;
                var items = new Style(typeof(ComboBoxItem)); items.Setters.Add(new Setter(Control.ForegroundProperty, primary)); items.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7, 5, 7, 5))); combo.ItemContainerStyle = items;
            }
            amount.Foreground = new SolidColorBrush(dark ? Color.FromRgb(107, 227, 193) : Color.FromRgb(9, 115, 93));
            chart.Dark = dark; chart.InvalidateVisual();
        }
        private void OnSourceInitialized(object sender, EventArgs e) {
            hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(hwnd, -20, new IntPtr(Native.GetWindowLongPtr(hwnd, -20).ToInt64() | 0x80));
            // Following uses SWP_NOACTIVATE. Explicit clicks may focus controls for keyboard access.
            if (!testMode) {
                StartBackend();
                hookCallback = delegate(IntPtr h, uint evt, IntPtr window, int obj, int child, uint thread, uint time) { if (window == petHandle && (evt == 0x8003 || evt == 0x8001)) Dispatcher.BeginInvoke(new Action(Hide)); };
                hook = Native.SetWinEventHook(0x8000, 0x800B, IntPtr.Zero, hookCallback, 0, 0, 0);
                followTimer.Tick += FollowPet; followTimer.Start();
            }
        }
        private void StartBackend() {
            string node = Environment.GetEnvironmentVariable("CODEX_STATUS_NODE") ?? "node.exe";
            var start = new ProcessStartInfo(node, "\"" + Path.Combine(root, "src", "backend", "main.mjs") + "\" --port 9337") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            backend = new Process { StartInfo = start, EnableRaisingEvents = true };
            backend.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) {
                if (String.IsNullOrEmpty(e.Data)) return;
                try { var value = Json.Serializer.DeserializeObject(e.Data); if (Json.Text(value, "type") == "view") Dispatcher.BeginInvoke(new Action(delegate { UpdateView(value); })); } catch { }
            };
            backend.ErrorDataReceived += delegate { }; // Protocol stderr is never displayed as user content.
            backend.Exited += delegate { Dispatcher.BeginInvoke(new Action(delegate { status.Text = "数据进程已退出，请重新启动挂件"; })); };
            try { backend.Start(); backend.BeginOutputReadLine(); backend.BeginErrorReadLine(); }
            catch { status.Text = "未能启动 Node.js，请运行构建检查"; }
        }
        private void SendSetting(string key, object value) { Send(new Dictionary<string, object> { { "type", "settings" }, { key, value } }); }
        private void Send(object message) { if (testMode || backend == null) return; try { backend.StandardInput.WriteLine(Json.Serializer.Serialize(message)); backend.StandardInput.Flush(); } catch { } }
        public void UpdateView(object data) {
            state = data; updating = true;
            var settings = Json.Get(data, "settings"); int nextDensity = (int)Json.Number(settings, "density"); if (nextDensity != density) SetDensity(nextDensity, false);
            dark = Json.Text(settings, "theme", "dark") != "light"; backgroundOpacity = Json.Number(settings, "opacity", 90); opacity.Value = backgroundOpacity;
            range.SelectedItem = Json.Text(settings, "range", "24h"); scope.SelectedIndex = Math.Max(0, Array.IndexOf(new[] { "current", "account", "tasks", "projects" }, Json.Text(settings, "scope", "current")));
            quotaLabel.Text = Json.Text(data, "quotaLabel", "CODEX · 周剩余"); amount.Text = Json.Get(data, "remaining") == null ? "—" : Json.Number(data, "remaining").ToString("0") + "%";
            forecast.Text = Json.Text(data, "forecast", "正在学习"); forecast.ToolTip = Json.Text(data, "forecastDetail");
            alert.Visibility = Json.Flag(data, "warning") ? Visibility.Visible : Visibility.Collapsed; alert.ToolTip = Json.Text(data, "forecastDetail");
            subTitle.Text = Json.Text(data, "subtitle"); subTitle.ToolTip = subTitle.Text; total.Text = Json.Get(data, "total") == null ? "— cr" : Json.Number(data, "total").ToString("N2") + " cr";
            var curves = new List<double?[]>(); var names = new List<string>(); var labels = new List<string>();
            foreach (var series in Json.Items(Json.Get(data, "series"))) { names.Add(Json.Text(series, "name")); labels.Add(Json.Text(series, "name") + "  " + (Json.Get(series, "total") == null ? "—" : Json.Number(series, "total").ToString("0.##")) + " cr");
                curves.Add(Json.Items(Json.Get(series, "points")).Select(x => x == null ? (double?)null : Convert.ToDouble(x)).ToArray()); }
            chart.Names = names.ToArray(); chart.Start = Epoch(Json.Number(data, "windowStart")); chart.End = Epoch(Json.Number(data, "windowEnd")); chart.SetSeries(curves);
            legend.Text = String.Join("  /  ", labels); reset.Text = "额度重置  " + Json.Text(data, "resetLabel");
            detailRows.Children.Clear(); foreach (var row in Json.Items(Json.Get(data, "details"))) {
                var grid = new DockPanel { Margin = new Thickness(0, 7, 0, 0) };
                var value = new TextBlock { Text = Json.Text(row, "value"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, Foreground = amount.Foreground };
                DockPanel.SetDock(value, Dock.Right); grid.Children.Add(value);
                grid.Children.Add(new TextBlock { Text = Json.Text(row, "name"), FontSize = 11, Foreground = quotaLabel.Foreground, TextTrimming = TextTrimming.CharacterEllipsis }); detailRows.Children.Add(grid);
            }
            other.Text = String.Join("\n", Json.Items(Json.Get(data, "other")).Select(Convert.ToString));
            coverage.Text = Json.Text(data, "coverage") + "\n费率版本 " + Json.Text(data, "rateVersion");
            status.Text = Json.Text(data, "status") + (Json.Get(data, "updated") == null ? "" : " · " + Json.Number(data, "updated").ToString("0") + " 秒前更新");
            ApplyTheme(); updating = false;
        }
        private static DateTime Epoch(double milliseconds) { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(milliseconds).ToLocalTime(); }
        private void FollowPet(object sender, EventArgs e) {
            var now = DateTime.UtcNow;
            if ((now - lastDiscover).TotalSeconds >= 1) {
                var ids = Native.CodexProcesses(); lastDiscover = now;
                if (ids.Count > 0) { hadCodex = true; missingSince = DateTime.MinValue; }
                else { if (missingSince == DateTime.MinValue) missingSince = now; if ((hadCodex || (now - missingSince).TotalSeconds > 30) && (now - missingSince).TotalSeconds > 3) { Close(); return; } }
                var mascot = Json.Get(state, "pet"); petHandle = Native.FindPet(ids, Json.Number(mascot, "innerWidth"), Json.Number(mascot, "dpr", 1));
            }
            var pet = Json.Get(state, "pet");
            if (petHandle == IntPtr.Zero || !Native.IsWindow(petHandle) || !Native.IsWindowVisible(petHandle) || pet == null || !Json.Flag(state, "petPageVisible")) { if (IsVisible) Hide(); firstPosition = true; return; }
            var origin = new Native.Point(); Native.ClientToScreen(petHandle, ref origin);
            double petDpr = Json.Number(pet, "dpr", 1), px = origin.X + Json.Number(pet, "x") * petDpr, py = origin.Y + Json.Number(pet, "y") * petDpr;
            double pw = Json.Number(pet, "width") * petDpr, ph = Json.Number(pet, "height") * petDpr;
            if (pw <= 0 || ph <= 0) { Hide(); return; }
            var work = Native.WorkArea((int)(px + pw / 2), (int)(py + ph / 2));
            double dpi = Native.GetDpiForWindow(petHandle) / 96.0;
            MaxHeight = Math.Max(150, work.Height / dpi - 24);
            double w = Width * dpi, h = Math.Max(100, ActualHeight) * dpi, gap = 10 * dpi;
            double x = px + pw / 2 - w / 2, y = py - h - gap;
            if (y < work.Top + gap) y = py + ph + gap;
            x = Math.Max(work.Left + gap, Math.Min(x, work.Right - w - gap)); y = Math.Max(work.Top + gap, Math.Min(y, work.Bottom - h - gap));
            double dt = Math.Min(.1, Math.Max(.001, (now - lastFrame).TotalSeconds)); lastFrame = now;
            double factor = SystemParameters.ClientAreaAnimation ? 1 - Math.Exp(-dt / .045) : 1;
            if (firstPosition || Math.Abs(x - physicalX) > 600 || Math.Abs(y - physicalY) > 600) { physicalX = x; physicalY = y; firstPosition = false; }
            else { physicalX += (x - physicalX) * factor; physicalY += (y - physicalY) * factor; }
            Native.SetWindowPos(hwnd, new IntPtr(-1), (int)Math.Round(physicalX), (int)Math.Round(physicalY), 0, 0, 0x0010 | 0x0001);
            if (!IsVisible) Show();
        }
        public void RenderTo(string filename) {
            chart.FinishAnimation();
            Measure(new Size(Width, 1000)); Arrange(new Rect(0, 0, Width, DesiredSize.Height)); UpdateLayout();
            var target = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth), (int)Math.Ceiling(ActualHeight), 96, 96, PixelFormats.Pbgra32); target.Render(this);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(target)); using (var stream = File.Create(filename)) png.Save(stream);
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
                    window.Close(); File.WriteAllText(Path.Combine(output, "ui-test.txt"), "PASS: all three densities rendered in light and dark themes; layout bounds valid."); return 0;
                }
                // Create the handle without presenting a floating window before the pet is found.
                new WindowInteropHelper(window).EnsureHandle(); app.Run(); return 0;
            } catch (Exception e) { Directory.CreateDirectory(Path.Combine(root, "artifacts")); File.WriteAllText(Path.Combine(root, "artifacts", "startup-error.log"), e.ToString()); return 1; }
            finally { if (created) mutex.ReleaseMutex(); mutex.Dispose(); }
        }
    }
}
