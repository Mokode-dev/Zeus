namespace Zeus;

/// <summary>
/// 一处界面绑定的句柄。释放后停止把通道事件推到控件，避免窗体关闭后仍更新已销毁的界面。
/// </summary>
public interface IUiBinding : IDisposable
{
}
