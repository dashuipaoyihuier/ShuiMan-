using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ShuiMan.Windows;
internal static class Dialogs
{
    public static string? Text(Window owner, string title, string label, string value = "", bool password = false)
    {
        var window = new Window { Owner = owner, Title = title, Width = 460, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(28) }; window.Content = panel;
        panel.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = label, Foreground = (Brush)owner.FindResource("MutedBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) });
        var input = new TextBox { Text = value }; var secret = new PasswordBox { Padding = new Thickness(8), Margin = new Thickness(3) };
        panel.Children.Add(password ? secret : input);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
        var ok = new Button { Content = "确定", IsDefault = true, MinWidth = 84, Style = (Style)owner.FindResource("PrimaryButton") }; ok.Click += (_, _) => window.DialogResult = true;
        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 84, Margin = new Thickness(0, 0, 9, 0) }; row.Children.Add(cancel); row.Children.Add(ok); panel.Children.Add(row);
        window.Loaded += (_, _) => { if (password) secret.Focus(); else { input.Focus(); input.SelectAll(); } };
        return window.ShowDialog() == true ? password ? secret.Password : input.Text : null;
    }
    public static (double Offset, double Scale)? Seam(Window owner, double offset, double scale)
    {
        var window = new Window { Owner = owner, Title = "跨页接缝微调", Width = 470, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(28) }; window.Content = panel;
        panel.Children.Add(new TextBlock { Text = "对齐跨页", FontSize = 23, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "调整右侧图像，使两页在接缝处自然衔接。", Foreground = (Brush)owner.FindResource("MutedBrush"), Margin = new Thickness(0, 10, 0, 26) });
        Slider AddSlider(string title, double value, double min, double max)
        {
            var label = new TextBlock { Margin = new Thickness(0, 0, 0, 12) };
            var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = .5, SmallChange = .5, LargeChange = 2, IsSnapToTickEnabled = true, Margin = new Thickness(0, 0, 0, 24) };
            label.Text = $"{title}    {slider.Value:0.#}%";
            slider.ValueChanged += (_, _) => label.Text = $"{title}    {slider.Value:0.#}%";
            panel.Children.Add(label); panel.Children.Add(slider); return slider;
        }
        var offsetInput = AddSlider("垂直偏移", offset * 100, -30, 30);
        var scaleInput = AddSlider("右图大小", scale * 100, 70, 130);
        var row = new DockPanel();
        var reset = new Button { Content = "恢复原始", Style = (Style)owner.FindResource("GhostButton") };
        reset.Click += (_, _) => { offsetInput.Value = 0; scaleInput.Value = 100; };
        DockPanel.SetDock(reset, Dock.Left); row.Children.Add(reset);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var apply = new Button { Content = "应用调整", IsDefault = true, Style = (Style)owner.FindResource("PrimaryButton") };
        apply.Click += (_, _) => window.DialogResult = true;
        actions.Children.Add(cancel); actions.Children.Add(apply); row.Children.Add(actions); panel.Children.Add(row);
        return window.ShowDialog() == true ? (offsetInput.Value / 100, scaleInput.Value / 100) : null;
    }
}
