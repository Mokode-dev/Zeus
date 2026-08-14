using System.Windows;
using System.Windows.Controls;

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
}
