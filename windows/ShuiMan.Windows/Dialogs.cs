using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace ShuiMan.Windows;
internal static class Dialogs
{
    public static string? Text(Window owner, string title, string label, string value = "", bool password = false)
    {
        var window = new Window { Owner = owner, Title = title, Width = 460, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(22) }; window.Content = panel;
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var input = new TextBox { Text = value }; var secret = new PasswordBox { Padding = new Thickness(8), Margin = new Thickness(3) };
        panel.Children.Add(password ? secret : input);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
        var ok = new Button { Content = "确定", IsDefault = true }; ok.Click += (_, _) => window.DialogResult = true;
        var cancel = new Button { Content = "取消", IsCancel = true }; row.Children.Add(ok); row.Children.Add(cancel); panel.Children.Add(row);
        window.Loaded += (_, _) => { if (password) secret.Focus(); else { input.Focus(); input.SelectAll(); } };
        return window.ShowDialog() == true ? password ? secret.Password : input.Text : null;
    }
    public static (double Offset, double Scale)? Seam(Window owner, double offset, double scale)
    {
        var raw = Text(owner, "跨页接缝微调", "输入右图的垂直偏移百分比与缩放百分比，用逗号分隔。\n例如：2, 100 表示右图向下移动 2%，大小不变。", $"{offset * 100:0.##}, {scale * 100:0.##}");
        if (raw == null) return null;
        var values = raw.Replace('，', ',').Split(',');
        if (values.Length == 2 && double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var a) && double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b) && double.IsFinite(a) && double.IsFinite(b) && Math.Abs(a) <= 30 && b >= 70 && b <= 130) return (a / 100, b / 100);
        MessageBox.Show(owner, "偏移范围为 -30 到 30，缩放范围为 70 到 130。", "输入无效"); return null;
    }
}
