using System.Windows.Forms;

namespace Zeus;

/// <summary>
/// WinForms 控件绑定。调用方不必再写 <c>InvokeRequired</c>。
/// </summary>
public static class WinFormsChannelExtensions
{
    /// <summary>
    /// 把通道收到的数据写到控件的 <see cref="Control.Text"/>。
    /// </summary>
    /// <param name="channel">数据来源。</param>
    /// <param name="control">目标标签、文本框等。</param>
    /// <param name="formatter">可选格式化；默认 ASCII / 十六进制自动选择。</param>
    /// <returns>绑定句柄，窗体关闭前应释放，或交给字段保存到窗口生命周期结束。</returns>
    public static IUiBinding BindText(
        this IChannel channel,
        Control control,
        Func<ReadOnlyMemory<byte>, string>? formatter = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        var dispatcher = new WinFormsUiDispatcher(control);
        return channel.BindText(dispatcher, text =>
        {
            if (!control.IsDisposed)
            {
                control.Text = text;
            }
        }, formatter);
    }

    /// <summary>
    /// <see cref="BindText"/> 的别名，对应手册中的 <c>BindTo</c> 神谕调用面。
    /// </summary>
    /// <param name="channel">数据来源。</param>
    /// <param name="control">目标控件。</param>
    /// <param name="formatter">可选格式化。</param>
    public static IUiBinding BindTo(
        this IChannel channel,
        Control control,
        Func<ReadOnlyMemory<byte>, string>? formatter = null)
        => channel.BindText(control, formatter);

    /// <summary>
    /// 把通道状态写到控件文本。
    /// </summary>
    /// <param name="channel">要观察的通道。</param>
    /// <param name="control">目标控件。</param>
    public static IUiBinding BindState(this IChannel channel, Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        var dispatcher = new WinFormsUiDispatcher(control);
        return channel.BindState(dispatcher, text =>
        {
            if (!control.IsDisposed)
            {
                control.Text = text;
            }
        });
    }

    /// <summary>
    /// 按通道状态控制控件 <see cref="Control.Enabled"/>。默认仅通道打开时启用。
    /// </summary>
    /// <param name="channel">要观察的通道。</param>
    /// <param name="control">目标控件。</param>
    /// <param name="isEnabled">状态到启用状态的映射；为空时仅打开态启用。</param>
    public static IUiBinding BindEnabled(
        this IChannel channel,
        Control control,
        Func<ChannelState, bool>? isEnabled = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        var dispatcher = new WinFormsUiDispatcher(control);
        return channel.BindEnabled(dispatcher, enabled =>
        {
            if (!control.IsDisposed)
            {
                control.Enabled = enabled;
            }
        }, isEnabled);
    }

    /// <summary>
    /// 创建可绑定投影，属性变更封送到该控件所在的界面线程。
    /// </summary>
    /// <param name="channel">要观察的通道。</param>
    /// <param name="control">用于取得 UI 线程的控件，通常是窗体本身。</param>
    public static ChannelBindingSource AsBindingSource(this IChannel channel, Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return channel.AsBindingSource(new WinFormsUiDispatcher(control));
    }

    /// <summary>
    /// 把点表中指定点的值写到控件文本。采集失败时显示错误说明。
    /// </summary>
    /// <param name="table">宿主点表。</param>
    /// <param name="pointName">短名或 <c>设备.点</c>。</param>
    /// <param name="control">目标控件。</param>
    /// <param name="formatter">可选格式化。</param>
    public static IUiBinding BindTo(
        this IPointTable table,
        string pointName,
        Control control,
        Func<object?, string>? formatter = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        return table.BindText(pointName, new WinFormsUiDispatcher(control), text =>
        {
            if (!control.IsDisposed)
            {
                control.Text = text;
            }
        }, formatter);
    }
}
