using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 监视通道故障并按指数退避自动 <see cref="IChannel.OpenAsync"/>。
/// 主动关闭、宿主停止或选项关闭时不会重连。
/// </summary>
internal sealed class ChannelReconnectService : IHostedService, IDisposable
{
    private readonly ChannelRegistry _channels;
    private readonly ChannelReconnectOptions _options;
    private readonly HostRunState _runState;
    private readonly ILogger<ChannelReconnectService> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, ReconnectAttempt> _attempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<IChannel> _subscribed = [];

    /// <summary>
    /// 初始化自动重连服务。
    /// </summary>
    public ChannelReconnectService(
        ChannelRegistry channels,
        ChannelReconnectOptions options,
        HostRunState runState,
        ILogger<ChannelReconnectService> logger)
    {
        _channels = channels;
        _options = options;
        _runState = runState;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _channels.Changed += OnRegistryChanged;
        _runState.Started += OnHostStarted;
        _runState.Stopped += OnHostStopped;
        foreach (var channel in _channels.All)
        {
            Subscribe(channel);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _channels.Changed -= OnRegistryChanged;
        _runState.Started -= OnHostStarted;
        _runState.Stopped -= OnHostStopped;
        CancelAll();
        foreach (var channel in _subscribed.ToArray())
        {
            Unsubscribe(channel);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose() => CancelAll();

    private void OnHostStarted(object? sender, EventArgs e)
    {
        foreach (var channel in _channels.All)
        {
            if (channel.State == ChannelState.Faulted)
            {
                Schedule(channel);
            }
        }
    }

    private void OnHostStopped(object? sender, EventArgs e) => CancelAll();

    private void OnRegistryChanged(object? sender, ChannelRegistryChangedEventArgs e)
    {
        if (e.Change == ChannelRegistryChange.Added)
        {
            Subscribe(e.Channel);
            if (e.Channel.State == ChannelState.Faulted && _runState.IsRunning)
            {
                Schedule(e.Channel);
            }

            return;
        }

        Unsubscribe(e.Channel);
        Cancel(e.Channel.Name);
    }

    private void Subscribe(IChannel channel)
    {
        lock (_gate)
        {
            if (!_subscribed.Add(channel))
            {
                return;
            }
        }

        channel.StateChanged += OnStateChanged;
    }

    private void Unsubscribe(IChannel channel)
    {
        lock (_gate)
        {
            _subscribed.Remove(channel);
        }

        channel.StateChanged -= OnStateChanged;
    }

    private void OnStateChanged(object? sender, ChannelStateChangedEventArgs e)
    {
        if (sender is not IChannel channel)
        {
            return;
        }

        if (e.Current == ChannelState.Faulted)
        {
            Schedule(channel);
            return;
        }

        // Opening 是本轮 OpenAsync 的中间态，不能取消正在使用的令牌。
        if (e.Current == ChannelState.Opening)
        {
            return;
        }

        Cancel(channel.Name);
        if (e.Current is ChannelState.Open or ChannelState.Closed)
        {
            ResetAttempt(channel.Name);
        }
    }

    private void Schedule(IChannel channel)
    {
        if (!_options.Enabled || !_runState.IsRunning)
        {
            return;
        }

        CancellationToken token;
        CancellationTokenSource cts;
        TimeSpan delay;
        int attempt;
        lock (_gate)
        {
            if (_attempts.TryGetValue(channel.Name, out var existing) && existing.Pending)
            {
                return;
            }

            existing?.Cts.Cancel();
            existing?.Cts.Dispose();

            var next = (existing?.Count ?? 0) + 1;
            cts = new CancellationTokenSource();
            _attempts[channel.Name] = new ReconnectAttempt(next, cts, Pending: true);
            token = cts.Token;
            delay = ComputeDelay(next);
            attempt = next;
        }

        _logger.LogWarning(
            "通道 {Channel} 已故障，将在 {Delay} ms 后进行第 {Attempt} 次自动重连。",
            channel.Name,
            (int)delay.TotalMilliseconds,
            attempt);
        _ = ReconnectAsync(channel, delay, cts, token);
    }

    private async Task ReconnectAsync(
        IChannel channel,
        TimeSpan delay,
        CancellationTokenSource owner,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            if (!_runState.IsRunning || channel.State != ChannelState.Faulted)
            {
                return;
            }

            await channel.OpenAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("通道 {Channel} 已自动重连。", channel.Name);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "通道 {Channel} 自动重连失败，将继续退避重试。", channel.Name);
        }
        finally
        {
            var shouldRetry = false;
            lock (_gate)
            {
                if (_attempts.TryGetValue(channel.Name, out var current) && ReferenceEquals(current.Cts, owner))
                {
                    _attempts[channel.Name] = current with { Pending = false };
                    shouldRetry = channel.State == ChannelState.Faulted
                        && _runState.IsRunning
                        && _options.Enabled
                        && !cancellationToken.IsCancellationRequested;
                }
            }

            // OpenAsync 失败时状态仍是 Faulted；打开过程中的 Opening 会取消本次排队。
            // 仅当本轮仍是当前尝试时再补一次，避免冲掉已经启动的下一轮。
            if (shouldRetry)
            {
                Schedule(channel);
            }
        }
    }

    private TimeSpan ComputeDelay(int attempt)
    {
        var initial = _options.InitialDelay < TimeSpan.Zero ? TimeSpan.FromSeconds(1) : _options.InitialDelay;
        var max = _options.MaxDelay < initial ? initial : _options.MaxDelay;
        var multiplier = _options.BackoffMultiplier < 1 ? 1 : _options.BackoffMultiplier;
        var factor = Math.Pow(multiplier, Math.Max(0, attempt - 1));
        if (double.IsInfinity(factor) || double.IsNaN(factor))
        {
            return max;
        }

        var millis = initial.TotalMilliseconds * factor;
        if (millis > max.TotalMilliseconds)
        {
            return max;
        }

        return TimeSpan.FromMilliseconds(millis);
    }

    private void ResetAttempt(string name)
    {
        lock (_gate)
        {
            if (_attempts.Remove(name, out var attempt))
            {
                attempt.Cts.Cancel();
                attempt.Cts.Dispose();
            }
        }
    }

    private void Cancel(string name)
    {
        lock (_gate)
        {
            if (_attempts.TryGetValue(name, out var attempt))
            {
                attempt.Cts.Cancel();
                _attempts[name] = attempt with { Pending = false };
            }
        }
    }

    private void CancelAll()
    {
        lock (_gate)
        {
            foreach (var attempt in _attempts.Values)
            {
                attempt.Cts.Cancel();
                attempt.Cts.Dispose();
            }

            _attempts.Clear();
        }
    }

    private sealed record ReconnectAttempt(int Count, CancellationTokenSource Cts, bool Pending);
}
