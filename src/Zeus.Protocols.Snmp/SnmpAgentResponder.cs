namespace Zeus;

/// <summary>SNMP v2c 内存虚拟 Agent，可直接交给 <c>AddVirtualChannel</c>。</summary>
public sealed class SnmpAgentResponder : IVirtualResponder
{
    private readonly SnmpAgentMemory _memory;
    private readonly string _community;
    private readonly string _writeCommunity;

    /// <summary>创建虚拟 SNMP Agent。</summary>
    public SnmpAgentResponder(SnmpAgentMemory? memory = null, string community = "public", string? writeCommunity = null)
    {
        _memory = memory ?? new SnmpAgentMemory();
        _community = string.IsNullOrWhiteSpace(community) ? "public" : community.Trim();
        _writeCommunity = string.IsNullOrWhiteSpace(writeCommunity) ? _community : writeCommunity!.Trim();
    }

    /// <summary>内存 MIB。</summary>
    public SnmpAgentMemory Memory => _memory;

    /// <inheritdoc />
    public ReadOnlyMemory<byte>? Respond(ReadOnlyMemory<byte> request)
    {
        SnmpMessage message;
        try
        {
            message = SnmpCodec.DecodeMessage(request.Span);
        }
        catch (Exception)
        {
            return null;
        }

        if (message.PduType == SnmpCodec.GetRequest)
        {
            if (!string.Equals(message.Community, _community, StringComparison.Ordinal))
            {
                return SnmpCodec.EncodeResponse(message.Community, message.RequestId, SnmpErrorStatus.AuthorizationError, 0, []);
            }

            return HandleGet(message);
        }

        if (message.PduType == SnmpCodec.SetRequest)
        {
            if (!string.Equals(message.Community, _writeCommunity, StringComparison.Ordinal))
            {
                return SnmpCodec.EncodeResponse(message.Community, message.RequestId, SnmpErrorStatus.AuthorizationError, 0, []);
            }

            return HandleSet(message);
        }

        return SnmpCodec.EncodeResponse(message.Community, message.RequestId, SnmpErrorStatus.GenErr, 0, []);
    }

    private byte[] HandleGet(SnmpMessage message)
    {
        var variables = new List<SnmpVariable>();
        for (var i = 0; i < message.Variables.Count; i++)
        {
            var variable = message.Variables[i];
            if (!_memory.TryGet(variable.Oid, out var value) || value is null)
            {
                return SnmpCodec.EncodeResponse(message.Community, message.RequestId, SnmpErrorStatus.NoSuchName, i + 1, []);
            }

            variables.Add(new SnmpVariable(variable.Oid, value));
        }

        return SnmpCodec.EncodeResponse(message.Community, message.RequestId, SnmpErrorStatus.NoError, 0, variables);
    }

    private byte[] HandleSet(SnmpMessage message)
    {
        for (var i = 0; i < message.Variables.Count; i++)
        {
            var variable = message.Variables[i];
            var status = _memory.TrySet(variable.Oid, variable.Value);
            if (status != SnmpErrorStatus.NoError)
            {
                return SnmpCodec.EncodeResponse(message.Community, message.RequestId, status, i + 1, []);
            }
        }

        return HandleGet(message);
    }
}
