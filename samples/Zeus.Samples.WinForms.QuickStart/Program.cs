namespace Zeus.Samples.WinForms.QuickStart;

/// <summary>
/// WinForms 示例入口。界面框架只负责消息循环，通道与生命周期交给 Zeus。
/// </summary>
internal static class Program
{
    /// <summary>
    /// 应用程序入口。
    /// </summary>
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
