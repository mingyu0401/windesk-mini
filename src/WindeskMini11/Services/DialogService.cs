using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace WindeskMini11.Services
{
    /// <summary>与主题一致的简易确认对话框。</summary>
    public static class DialogService
    {
        public static bool Confirm(Window? owner, string title, string message,
            string okText = "确定", string cancelText = "取消")
        {
            var dialog = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                SizeToContent = SizeToContent.Height,
                Width = 460,
                WindowStartupLocation = owner == null
                    ? WindowStartupLocation.CenterScreen
                    : WindowStartupLocation.CenterOwner
            };

            if (owner != null) dialog.Owner = owner;

            var card = new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(24, 20, 24, 20),
                Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 5, Opacity = 0.35, Color = Colors.Black }
            };
            card.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

            var panel = new StackPanel();

            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

            var bodyText = new TextBlock
            {
                Text = message,
                FontSize = 12.5,
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20
            };
            bodyText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };

            var cancelButton = new Button
            {
                Content = cancelText,
                Style = Application.Current.TryFindResource("BtnSecondary") as Style,
                MinWidth = 76,
                IsCancel = true
            };

            var okButton = new Button
            {
                Content = okText,
                Style = Application.Current.TryFindResource("BtnPrimary") as Style,
                MinWidth = 76,
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = true
            };

            var result = false;
            okButton.Click += (_, _) =>
            {
                result = true;
                dialog.Close();
            };
            cancelButton.Click += (_, _) => dialog.Close();

            buttons.Children.Add(cancelButton);
            buttons.Children.Add(okButton);

            panel.Children.Add(titleText);
            panel.Children.Add(bodyText);
            panel.Children.Add(buttons);
            card.Child = panel;

            var wrapper = new Border { Margin = new Thickness(22), Child = card };
            wrapper.MouseLeftButtonDown += (_, _) =>
            {
                try
                {
                    dialog.DragMove();
                }
                catch
                {
                    // 忽略拖拽异常
                }
            };

            dialog.Content = wrapper;
            dialog.ShowDialog();

            return result;
        }
    }
}
