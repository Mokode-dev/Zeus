using System.Drawing;
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

    /// <summary>
    /// 把点表中指定点的完整快照推到界面线程。
    /// </summary>
    /// <param name="table">宿主点表。</param>
    /// <param name="pointName">短名或 <c>设备.点</c>。</param>
    /// <param name="control">用于取得 UI 线程的控件。</param>
    /// <param name="setSnapshot">在界面线程上接收快照。</param>
    public static IUiBinding BindSnapshot(
        this IPointTable table,
        string pointName,
        Control control,
        Action<PointSnapshot> setSnapshot)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(setSnapshot);
        return table.BindSnapshot(pointName, new WinFormsUiDispatcher(control), snapshot =>
        {
            if (!control.IsDisposed)
            {
                setSnapshot(snapshot);
            }
        });
    }

    /// <summary>
    /// 创建单个点的可绑定投影，属性变更封送到该控件所在的界面线程。
    /// </summary>
    /// <param name="table">宿主点表。</param>
    /// <param name="pointName">短名或 <c>设备.点</c>。</param>
    /// <param name="control">用于取得 UI 线程的控件，通常是窗体本身。</param>
    /// <param name="formatter">成功值到文本的转换。</param>
    public static PointBindingSource AsBindingSource(
        this IPointTable table,
        string pointName,
        Control control,
        Func<object?, string>? formatter = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        return table.AsBindingSource(pointName, new WinFormsUiDispatcher(control), formatter);
    }

    /// <summary>
    /// 按点快照控制控件 <see cref="Control.Enabled"/>。默认仅可写且无错误时启用。
    /// </summary>
    /// <param name="table">宿主点表。</param>
    /// <param name="pointName">短名或 <c>设备.点</c>。</param>
    /// <param name="control">目标控件。</param>
    /// <param name="isEnabled">快照到启用状态的映射；为空时使用可写且无错误。</param>
    public static IUiBinding BindEnabled(
        this IPointTable table,
        string pointName,
        Control control,
        Func<PointSnapshot, bool>? isEnabled = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        return table.BindEnabled(pointName, new WinFormsUiDispatcher(control), enabled =>
        {
            if (!control.IsDisposed)
            {
                control.Enabled = enabled;
            }
        }, isEnabled);
    }

    /// <summary>
    /// 按点的报警和错误状态切换控件背景色。错误优先于报警状态。
    /// </summary>
    /// <param name="table">宿主点表。</param>
    /// <param name="pointName">短名或 <c>设备.点</c>。</param>
    /// <param name="control">目标控件。</param>
    /// <param name="normalColor">正常颜色；为空时使用控件当前背景色。</param>
    /// <param name="lowColor">低报颜色。</param>
    /// <param name="highColor">高报颜色。</param>
    /// <param name="errorColor">错误颜色。</param>
    /// <param name="unknownColor">未知颜色。</param>
    public static IUiBinding BindAlarmBackColor(
        this IPointTable table,
        string pointName,
        Control control,
        Color? normalColor = null,
        Color? lowColor = null,
        Color? highColor = null,
        Color? errorColor = null,
        Color? unknownColor = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        var normal = normalColor ?? control.BackColor;
        var low = lowColor ?? Color.LightBlue;
        var high = highColor ?? Color.MistyRose;
        var error = errorColor ?? Color.LightPink;
        var unknown = unknownColor ?? normal;

        return table.BindSnapshot(pointName, control, snapshot =>
            control.BackColor = SelectAlarmColor(snapshot, normal, low, high, error, unknown));
    }

    /// <summary>
    /// 按点的报警和错误状态切换控件前景色。错误优先于报警状态。
    /// </summary>
    /// <param name="table">宿主点表。</param>
    /// <param name="pointName">短名或 <c>设备.点</c>。</param>
    /// <param name="control">目标控件。</param>
    /// <param name="normalColor">正常颜色；为空时使用控件当前前景色。</param>
    /// <param name="lowColor">低报颜色。</param>
    /// <param name="highColor">高报颜色。</param>
    /// <param name="errorColor">错误颜色。</param>
    /// <param name="unknownColor">未知颜色。</param>
    public static IUiBinding BindAlarmForeColor(
        this IPointTable table,
        string pointName,
        Control control,
        Color? normalColor = null,
        Color? lowColor = null,
        Color? highColor = null,
        Color? errorColor = null,
        Color? unknownColor = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        var normal = normalColor ?? control.ForeColor;
        var low = lowColor ?? Color.RoyalBlue;
        var high = highColor ?? Color.Firebrick;
        var error = errorColor ?? Color.DarkRed;
        var unknown = unknownColor ?? Color.DimGray;

        return table.BindSnapshot(pointName, control, snapshot =>
            control.ForeColor = SelectAlarmColor(snapshot, normal, low, high, error, unknown));
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
        TextBoxBase textBox,
        ButtonBase button,
        Func<string, object>? parser = null,
        Action<Exception>? onError = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(textBox);
        ArgumentNullException.ThrowIfNull(button);
        parser ??= static text => text;

        async void OnClick(object? sender, EventArgs e)
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

    private static Color SelectAlarmColor(
        PointSnapshot snapshot,
        Color normal,
        Color low,
        Color high,
        Color error,
        Color unknown)
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
