using System.Windows.Forms;

namespace Zeus;

/// <summary>
/// 基于 <see cref="Control"/> 的 WinForms 调度器。
/// 控件已销毁或尚未创建句柄时会丢弃投递，避免向已关闭窗口回写。
/// </summary>
public sealed class WinFormsUiDispatcher : IUiDispatcher
{
    private readonly Control _control;

    /// <summary>
    /// 使用指定控件所在的 UI 线程。
    /// </summary>
    /// <param name="control">任意已加入窗体树的控件，通常是主窗体本身。</param>
    public WinFormsUiDispatcher(Control control)
    {
        _control = control ?? throw new ArgumentNullException(nameof(control));
    }

    /// <inheritdoc />
    public bool CheckAccess() => !_control.InvokeRequired;

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_control.IsDisposed || !_control.IsHandleCreated)
        {
            return;
        }

        try
        {
            _control.BeginInvoke(action);
        }
        catch (ObjectDisposedException)
        {
            // 窗口在投递瞬间关闭：丢弃本次更新即可。
        }
        catch (InvalidOperationException)
        {
            // 句柄已销毁但 IsHandleCreated 尚未反映：同样丢弃。
        }
    }
}
