using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using PublicApiGenerator;

namespace Zeus.Api.Tests;

/// <summary>
/// 对照已提交的基线文件，检查各程序集公开 API 是否被意外改动。
/// 有意新增或调整公开表面时，请同步更新 <c>Baselines</c> 目录中的对应文件。
/// </summary>
public sealed class PublicApiBaselineTests
{
    /// <summary>契约层公开表面。</summary>
    [Fact]
    public void Abstractions_matches_baseline() => AssertMatchesBaseline(typeof(IZeusHost).Assembly);

    /// <summary>运行时内核公开表面。包标识为 Zeus.Runtime。</summary>
    [Fact]
    public void Runtime_matches_baseline() => AssertMatchesBaseline(typeof(ChannelBase).Assembly);

    /// <summary>宿主与采集循环公开表面。</summary>
    [Fact]
    public void Hosting_matches_baseline() => AssertMatchesBaseline(typeof(ZeusHost).Assembly);

    /// <summary>串口、TCP、UDP 与虚拟通道公开表面。</summary>
    [Fact]
    public void Communications_matches_baseline() => AssertMatchesBaseline(typeof(VirtualChannel).Assembly);

    /// <summary>JSON 配置公开表面。</summary>
    [Fact]
    public void Configuration_matches_baseline() => AssertMatchesBaseline(typeof(ZeusConfigurationLoader).Assembly);

    /// <summary>自定义帧公开表面。</summary>
    [Fact]
    public void Framing_matches_baseline() => AssertMatchesBaseline(typeof(FrameSession).Assembly);

    /// <summary>Modbus 公开表面。</summary>
    [Fact]
    public void Modbus_matches_baseline() => AssertMatchesBaseline(typeof(ModbusDevice).Assembly);

    /// <summary>Omron FINS 公开表面。</summary>
    [Fact]
    public void Fins_matches_baseline() => AssertMatchesBaseline(typeof(FinsDevice).Assembly);

    /// <summary>Mitsubishi MC 公开表面。</summary>
    [Fact]
    public void Mc_matches_baseline() => AssertMatchesBaseline(typeof(McDevice).Assembly);

    /// <summary>Siemens S7 公开表面。</summary>
    [Fact]
    public void S7_matches_baseline() => AssertMatchesBaseline(typeof(S7Device).Assembly);

    /// <summary>UI 无关绑定抽象公开表面。</summary>
    [Fact]
    public void PresentationAbstractions_matches_baseline() => AssertMatchesBaseline(typeof(IUiDispatcher).Assembly);

    /// <summary>WinForms 适配器公开表面。</summary>
    [Fact]
    public void WinForms_matches_baseline() => AssertMatchesBaseline(typeof(WinFormsHostExtensions).Assembly);

    /// <summary>WPF 适配器公开表面。</summary>
    [Fact]
    public void Wpf_matches_baseline() => AssertMatchesBaseline(typeof(WpfHostExtensions).Assembly);

    /// <summary>
    /// 生成当前程序集的公开 API 文本，并与仓库中的基线逐字比较。
    /// </summary>
    /// <param name="assembly">待检查的已发布程序集。</param>
    private static void AssertMatchesBaseline(Assembly assembly)
    {
        var actual = assembly.GeneratePublicApi(new ApiGeneratorOptions
        {
            IncludeAssemblyAttributes = false,
            ExcludeAttributes =
            [
                typeof(InternalsVisibleToAttribute).FullName!,
            ],
        }).ReplaceLineEndings("\n");

        var fileName = assembly.GetName().Name + ".txt";
        var baselinePath = Path.Combine(AppContext.BaseDirectory, "Baselines", fileName);
        var sourcePath = FindSourceBaseline(fileName);

        // 设置 ZEUS_WRITE_API_BASELINES=1 后重新生成基线，仅用于有意调整公开 API。
        if (string.Equals(Environment.GetEnvironmentVariable("ZEUS_WRITE_API_BASELINES"), "1", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(sourcePath, actual);
        }

        Assert.True(File.Exists(baselinePath) || File.Exists(sourcePath), $"缺少公开 API 基线：{fileName}");
        var expected = File.ReadAllText(File.Exists(baselinePath) ? baselinePath : sourcePath).ReplaceLineEndings("\n");
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// 定位仓库中的基线源文件，避免只改到测试输出目录。
    /// </summary>
    private static string FindSourceBaseline(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            // 只认测试项目源目录，避免命中 bin 下复制出来的基线。
            if (directory.Name == "Zeus.Api.Tests"
                && !directory.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !directory.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(directory.FullName, "Baselines", fileName);
            }

            directory = directory.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "Baselines", fileName);
    }
}
