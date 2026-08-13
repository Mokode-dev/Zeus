using System.Windows;

namespace Zeus;

/// <summary>
/// 将 Zeus 宿主挂到 WPF 窗口生命周期：Loaded 时启动，Closed 时释放。
/// </summary>
public static class WpfHostExtensions
{
    /// <summary>
    /// 把已创建的宿主交给窗口管理。
    /// </summary>
    /// <param name="window">主窗口。</param>
    /// <param name="host">尚未启动的宿主。</param>
    public static UiHostAttachment AttachZeus(this Window window, IZeusHost host)
    {
        ArgumentNullException.ThrowIfNull(window);
        var attachment = new UiHostAttachment(host);

        async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await attachment.StartAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    window,
                    ex.Message,
                    "Zeus 宿主启动失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        void OnClosed(object? sender, EventArgs e)
        {
            window.Loaded -= OnLoaded;
            window.Closed -= OnClosed;
            attachment.DisposeBlocking();
        }

        window.Loaded += OnLoaded;
        window.Closed += OnClosed;

        if (window.IsLoaded)
        {
            OnLoaded(window, new RoutedEventArgs());
        }

        return attachment;
    }

    /// <summary>
    /// 在窗口内创建并挂接宿主。
    /// </summary>
    /// <param name="window">主窗口。</param>
    /// <param name="configure">通道与设备注册。</param>
    public static UiHostAttachment AttachZeus(this Window window, Action<ZeusHostBuilder>? configure)
        => window.AttachZeus(ZeusHost.Create(configure));
}
