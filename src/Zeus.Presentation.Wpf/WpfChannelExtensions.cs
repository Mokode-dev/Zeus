using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Zeus;

/// <summary>
/// WPF 文本控件绑定与可绑定源。
/// </summary>
public static class WpfChannelExtensions
{
    /// <summary>
    /// 把通道收到的数据写到 <see cref="TextBlock.Text"/>。
    /// </summary>
    /// <param name="channel">数据来源。</param>
    /// <param name="textBlock">目标文本块。</param>
    /// <param name="formatter">可选格式化。</param>
    public static IUiBinding BindText(
        this IChannel channel,
        TextBlock textBlock,
        Func<ReadOnlyMemory<byte>, string>? formatter = null)
    {
        ArgumentNullException.ThrowIfNull(textBlock);
        var dispatcher = new WpfUiDispatcher(textBlock.Dispatcher);
        return channel.BindText(dispatcher, text => textBlock.Text = text, formatter);
    }

    /// <summary>
    /// <see cref="BindText"/> 的别名，对应手册中的 <c>BindTo</c>。
    /// </summary>
    /// <param name="channel">数据来源。</param>
    /// <param name="textBlock">目标文本块。</param>
    /// <param name="formatter">可选格式化。</param>
    public static IUiBinding BindTo(
        this IChannel channel,
        TextBlock textBlock,
        Func<ReadOnlyMemory<byte>, string>? formatter = null)
        => channel.BindText(textBlock, formatter);

    /// <summary>
    /// 把通道状态写到文本块。
    /// </summary>
    /// <param name="channel">要观察的通道。</param>
    /// <param name="textBlock">目标文本块。</param>
    public static IUiBinding BindState(this IChannel channel, TextBlock textBlock)
    {
        ArgumentNullException.ThrowIfNull(textBlock);
        var dispatcher = new WpfUiDispatcher(textBlock.Dispatcher);
        return channel.BindState(dispatcher, text => textBlock.Text = text);
    }

    /// <summary>
    /// 按通道状态控制元素 <see cref="UIElement.IsEnabled"/>。默认仅通道打开时启用。
    /// </summary>
    /// <param name="channel">要观察的通道。</param>
    /// <param name="element">目标元素。</param>
    /// <param name="isEnabled">状态到启用状态的映射；为空时仅打开态启用。</param>
    public static IUiBinding BindEnabled(
        this IChannel channel,
        UIElement element,
        Func<ChannelState, bool>? isEnabled = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        var dispatcher = new WpfUiDispatcher(element.Dispatcher);
        return channel.BindEnabled(dispatcher, enabled => element.IsEnabled = enabled, isEnabled);
    }

    /// <summary>
    /// 创建可直接作为 <c>DataContext</c> 的投影，属性变更封送到该元素所在的界面线程。
    /// </summary>
    /// <param name="channel">要观察的通道。</param>
    /// <param name="element">用于取得 <c>Dispatcher</c> 的窗口或控件。</param>
    public static ChannelBindingSource AsBindingSource(this IChannel channel, FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return channel.AsBindingSource(new WpfUiDispatcher(element.Dispatcher));
    }

    /// <summary>
    /// 把点表中指定点的值写到文本块。采集失败时显示错误说明。
    /// </summary>
    /// <param name="table">宿主点表。</param>
    /// <param name="pointName">短名或 <c>设备.点</c>。</param>
    /// <param name="textBlock">目标文本块。</param>
    /// <param name="formatter">可选格式化。</param>
    public static IUiBinding BindTo(
        this IPointTable table,
        string pointName,
        TextBlock textBlock,
        Func<object?, string>? formatter = null)
    {
        ArgumentNullException.ThrowIfNull(textBlock);
        return table.BindText(
            pointName,
            new WpfUiDispatcher(textBlock.Dispatcher),
            text => textBlock.Text = text,
            formatter);
    }

    /// <summary>
    /// 把点表中指定点的完整快照推到界面线程。
    /// </summary>
    /// <param name="table">宿主点表。</param>
    /// <param name="pointName">短名或 <c>设备.点</c>。</param>
    /// <param name="element">用于取得 <c>Dispatcher</c> 的窗口或控件。</param>
    /// <param name="setSnapshot">在界面线程上接收快照。</param>
    public static IUiBinding BindSnapshot(
        this IPointTable table,
        string pointName,
        FrameworkElement element,
        Action<PointSnapshot> setSnapshot)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(setSnapshot);
        return table.BindSnapshot(pointName, new WpfUiDispatcher(element.Dispatcher), setSnapshot);
    }

    /// <summary>
    /// 创建单个点的可绑定投影，属性变更封送到该元素所在的界面线程。
    /// </summary>
    /// <param name="table">宿主点表。</param>
    /// <param name="pointName">短名或 <c>设备.点</c>。</param>
    /// <param name="element">用于取得 <c>Dispatcher</c> 的窗口或控件。</param>
    /// <param name="formatter">成功值到文本的转换。</param>
    public static PointBindingSource AsBindingSource(
        this IPointTable table,
        string pointName,
        FrameworkElement element,
        Func<object?, string>? formatter = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        return table.AsBindingSource(pointName, new WpfUiDispatcher(element.Dispatcher), formatter);
    }

    /// <summary>
    /// 把点表中指定点的最近成功采样历史推到界面线程。
    /// </summary>
    /// <param name="table">宿主点表。</param>
    /// <param name="pointName">短名或 <c>设备.点</c>。</param>
    /// <param name="element">用于取得 <c>Dispatcher</c> 的窗口或控件。</param>
    /// <param name="setHistory">在界面线程上接收历史，顺序从旧到新。</param>
    public static IUiBinding BindHistory(
        this IPointTable table,
        string pointName,
        FrameworkElement element,
        Action<IReadOnlyList<PointSnapshot>> setHistory)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(setHistory);
        return table.BindHistory(pointName, new WpfUiDispatcher(element.Dispatcher), setHistory);
    }

    /// <summary>
    /// 创建单个点的历史采样投影，属性变更封送到该元素所在的界面线程。
    /// </summary>
    /// <param name="table">宿主点表。</param>
    /// <param name="pointName">短名或 <c>设备.点</c>。</param>
    /// <param name="element">用于取得 <c>Dispatcher</c> 的窗口或控件。</param>
    public static PointHistoryBindingSource AsHistoryBindingSource(
        this IPointTable table,
        string pointName,
        FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return table.AsHistoryBindingSource(pointName, new WpfUiDispatcher(element.Dispatcher));
    }

    /// <summary>
    /// 按点快照控制元素 <see cref="UIElement.IsEnabled"/>。默认仅可写且无错误时启用。
    /// </summary>
    /// <param name="table">宿主点表。</param>
    /// <param name="pointName">短名或 <c>设备.点</c>。</param>
    /// <param name="element">目标元素。</param>
    /// <param name="isEnabled">快照到启用状态的映射；为空时使用可写且无错误。</param>
    public static IUiBinding BindEnabled(
        this IPointTable table,
        string pointName,
        UIElement element,
        Func<PointSnapshot, bool>? isEnabled = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        return table.BindEnabled(
            pointName,
            new WpfUiDispatcher(element.Dispatcher),
            enabled => element.IsEnabled = enabled,
            isEnabled);
    }

    /// <summary>
    /// 按点的报警和错误状态切换控件背景色。错误优先于报警状态。
    /// </summary>
    /// <param name="table">宿主点表。</param>
    /// <param name="pointName">短名或 <c>设备.点</c>。</param>
    /// <param name="control">目标控件。</param>
    /// <param name="normalBrush">正常画刷；为空时使用控件当前背景。</param>
    /// <param name="lowBrush">低报画刷。</param>
    /// <param name="highBrush">高报画刷。</param>
    /// <param name="errorBrush">错误画刷。</param>
    /// <param name="unknownBrush">未知画刷。</param>
    public static IUiBinding BindAlarmBackground(
        this IPointTable table,
        string pointName,
        Control control,
        Brush? normalBrush = null,
        Brush? lowBrush = null,
        Brush? highBrush = null,
        Brush? errorBrush = null,
        Brush? unknownBrush = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        var normal = normalBrush ?? control.Background ?? Brushes.Transparent;
        var low = lowBrush ?? Brushes.LightBlue;
        var high = highBrush ?? Brushes.MistyRose;
        var error = errorBrush ?? Brushes.LightPink;
        var unknown = unknownBrush ?? normal;

        return table.BindSnapshot(pointName, control, snapshot =>
            control.Background = SelectAlarmBrush(snapshot, normal, low, high, error, unknown));
    }

    /// <summary>
    /// 按点的报警和错误状态切换文本块背景色。错误优先于报警状态。
    /// </summary>
    /// <param name="table">宿主点表。</param>
    /// <param name="pointName">短名或 <c>设备.点</c>。</param>
    /// <param name="textBlock">目标文本块。</param>
    /// <param name="normalBrush">正常画刷；为空时使用文本块当前背景。</param>
    /// <param name="lowBrush">低报画刷。</param>
    /// <param name="highBrush">高报画刷。</param>
    /// <param name="errorBrush">错误画刷。</param>
    /// <param name="unknownBrush">未知画刷。</param>
    public static IUiBinding BindAlarmBackground(
        this IPointTable table,
        string pointName,
        TextBlock textBlock,
        Brush? normalBrush = null,
        Brush? lowBrush = null,
        Brush? highBrush = null,
        Brush? errorBrush = null,
        Brush? unknownBrush = null)
    {
        ArgumentNullException.ThrowIfNull(textBlock);
        var normal = normalBrush ?? textBlock.Background ?? Brushes.Transparent;
        var low = lowBrush ?? Brushes.LightBlue;
        var high = highBrush ?? Brushes.MistyRose;
        var error = errorBrush ?? Brushes.LightPink;
        var unknown = unknownBrush ?? normal;

        return table.BindSnapshot(pointName, textBlock, snapshot =>
            textBlock.Background = SelectAlarmBrush(snapshot, normal, low, high, error, unknown));
    }

    /// <summary>
    /// 按点的报警和错误状态切换控件前景色。错误优先于报警状态。
    /// </summary>
    /// <param name="table">宿主点表。</param>
    /// <param name="pointName">短名或 <c>设备.点</c>。</param>
    /// <param name="control">目标控件。</param>
    /// <param name="normalBrush">正常画刷；为空时使用控件当前前景。</param>
    /// <param name="lowBrush">低报画刷。</param>
    /// <param name="highBrush">高报画刷。</param>
    /// <param name="errorBrush">错误画刷。</param>
    /// <param name="unknownBrush">未知画刷。</param>
    public static IUiBinding BindAlarmForeground(
        this IPointTable table,
        string pointName,
        Control control,
        Brush? normalBrush = null,
        Brush? lowBrush = null,
        Brush? highBrush = null,
        Brush? errorBrush = null,
        Brush? unknownBrush = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        var normal = normalBrush ?? control.Foreground;
        var low = lowBrush ?? Brushes.RoyalBlue;
        var high = highBrush ?? Brushes.Firebrick;
        var error = errorBrush ?? Brushes.DarkRed;
        var unknown = unknownBrush ?? Brushes.DimGray;

        return table.BindSnapshot(pointName, control, snapshot =>
            control.Foreground = SelectAlarmBrush(snapshot, normal, low, high, error, unknown));
    }

    /// <summary>
    /// 按点的报警和错误状态切换文本块前景色。错误优先于报警状态。
    /// </summary>
    /// <param name="table">宿主点表。</param>
    /// <param name="pointName">短名或 <c>设备.点</c>。</param>
    /// <param name="textBlock">目标文本块。</param>
    /// <param name="normalBrush">正常画刷；为空时使用文本块当前前景。</param>
    /// <param name="lowBrush">低报画刷。</param>
    /// <param name="highBrush">高报画刷。</param>
    /// <param name="errorBrush">错误画刷。</param>
    /// <param name="unknownBrush">未知画刷。</param>
    public static IUiBinding BindAlarmForeground(
        this IPointTable table,
        string pointName,
        TextBlock textBlock,
        Brush? normalBrush = null,
        Brush? lowBrush = null,
        Brush? highBrush = null,
        Brush? errorBrush = null,
        Brush? unknownBrush = null)
    {
        ArgumentNullException.ThrowIfNull(textBlock);
        var normal = normalBrush ?? textBlock.Foreground;
        var low = lowBrush ?? Brushes.RoyalBlue;
        var high = highBrush ?? Brushes.Firebrick;
        var error = errorBrush ?? Brushes.DarkRed;
        var unknown = unknownBrush ?? Brushes.DimGray;

        return table.BindSnapshot(pointName, textBlock, snapshot =>
            textBlock.Foreground = SelectAlarmBrush(snapshot, normal, low, high, error, unknown));
    }

    /// <summary>
    /// 把按钮点击绑定为点表写回。释放返回值会退订点击事件和默认启用状态绑定。
    /// </summary>
    /// <param name="table">宿主点表。</param>
    /// <param name="pointName">短名或 <c>设备.点</c>。</param>
    /// <param name="textBox">输入控件。</param>
    /// <param name="button">触发写回的按钮。</param>
    /// <param name="parser">把输入文本转换为工程值；为空时按字符串写回。</param>
    /// <param name="onError">写回或转换失败时的回调。</param>
    /// <param name="cancellationToken">取消本次写回。</param>
    public static IUiBinding BindWriteBack(
        this IPointTable table,
        string pointName,
        TextBox textBox,
        ButtonBase button,
        Func<string, object>? parser = null,
        Action<Exception>? onError = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(textBox);
        ArgumentNullException.ThrowIfNull(button);
        parser ??= static text => text;

        async void OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var value = parser(textBox.Text);
                await table.WriteAsync(pointName, value, cancellationToken).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
            }
        }

        button.Click += OnClick;
        var enabledBinding = table.BindEnabled(pointName, button);
        return new DelegateUiBinding(() =>
        {
            button.Click -= OnClick;
            enabledBinding.Dispose();
        });
    }

    private static Brush SelectAlarmBrush(
        PointSnapshot snapshot,
        Brush normal,
        Brush low,
        Brush high,
        Brush error,
        Brush unknown)
    {
        if (snapshot.Error is not null)
        {
            return error;
        }

        return snapshot.AlarmState switch
        {
            PointAlarmState.Low => low,
            PointAlarmState.High => high,
            PointAlarmState.Unknown => unknown,
            _ => normal
        };
    }
}
