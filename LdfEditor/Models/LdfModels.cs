namespace LdfEditor.Models;

public sealed class LdfDatabase
{
    public string FilePath { get; set; } = "";
    public string Name { get; set; } = "";
    public string ProtocolVersion { get; set; } = "";
    public string LanguageVersion { get; set; } = "";
    public string FileRevision { get; set; } = "";
    public double SpeedKbps { get; set; }
    public string ChannelName { get; set; } = "";
    public string ChannelPostfix { get; set; } = "";
    public string Comment { get; set; } = "";
    public bool IsMotorolaByteOrder { get; set; }
    public string Master { get; set; } = "";
    public double TimebaseMs { get; set; }
    public double JitterMs { get; set; }
    public uint? J2602HeaderLength { get; set; }
    public double? J2602MasterResponseTolerance { get; set; }
    public List<string> Slaves { get; } = new();
    public Dictionary<string, int> DiagnosticAddresses { get; } = new(StringComparer.Ordinal);
    public List<LdfSignal> Signals { get; } = new();
    public List<LdfSignal> DiagnosticSignals { get; } = new();
    public List<LdfFrame> Frames { get; } = new();
    public List<LdfSporadicFrame> SporadicFrames { get; } = new();
    public List<LdfEventTriggeredFrame> EventTriggeredFrames { get; } = new();
    public List<LdfScheduleTable> Schedules { get; } = new();
    public List<LdfNodeAttr> NodeAttributes { get; } = new();
    public List<LdfEncoding> Encodings { get; } = new();
    public List<LdfRepresentation> Representations { get; } = new();
    public List<LdfSignalGroup> SignalGroups { get; } = new();
    public List<LdfCompositeConfig> CompositeConfigs { get; } = new();

    public LdfSignal? FindSignal(string name) =>
        Signals.FirstOrDefault(s => s.Name == name)
        ?? DiagnosticSignals.FirstOrDefault(s => s.Name == name);

    public LdfFrame? FindFrame(string name) =>
        Frames.FirstOrDefault(f => f.Name == name);

    public LdfEncoding? FindEncodingForSignal(string signalName)
    {
        foreach (var r in Representations)
        {
            if (!r.Signals.Contains(signalName)) continue;
            var enc = Encodings.FirstOrDefault(e => e.Name == r.Encoding);
            if (enc != null) return enc;
        }
        return null;
    }
}

public sealed class LdfFrame
{
    public string Name { get; set; } = "";
    public int Id { get; set; }
    public string Publisher { get; set; } = "";
    public int Length { get; set; } = 8;
    public string Kind { get; set; } = "Unconditional"; // Unconditional / Diagnostic
    public bool IsDiagnostic => Kind == "Diagnostic";
    public List<LdfFrameSignal> Signals { get; } = new();
}

public sealed class LdfFrameSignal
{
    public string Name { get; set; } = "";
    public int StartBit { get; set; }
}

public sealed class LdfSignal
{
    public string Name { get; set; } = "";
    public int SizeBits { get; set; }
    public string InitValue { get; set; } = "";
    public string Publisher { get; set; } = "";
    public List<string> Subscribers { get; } = new();
}

public sealed class LdfSporadicFrame
{
    public string Name { get; set; } = "";
    public List<string> FrameNames { get; } = new();
}

public sealed class LdfEventTriggeredFrame
{
    public string Name { get; set; } = "";
    public string CollisionResolvingSchedule { get; set; } = "";
    public int Id { get; set; }
    public List<string> FrameNames { get; } = new();
}

public sealed class LdfScheduleTable
{
    public string Name { get; set; } = "";
    public List<LdfScheduleSlot> Slots { get; } = new();
}

public sealed class LdfScheduleSlot
{
    public string Command { get; set; } = "";
    public string Kind { get; set; } = "";
    public string DisplayType { get; set; } = "";
    public string FrameName { get; set; } = "";
    public string NodeName { get; set; } = "";
    public string AssignedFrame { get; set; } = "";
    public int StartIndex { get; set; } = -1;
    public bool HasExplicitPids { get; set; }
    public List<int> Pids { get; } = new();
    public List<int> IntArgs { get; } = new();
    public double DelayMs { get; set; }

    /* Back-compat for older UI bindings */
    public string CommandOrFrame => string.IsNullOrEmpty(FrameName) ? Command : FrameName;
    public string Type => string.IsNullOrEmpty(DisplayType) ? (string.IsNullOrEmpty(Kind) ? "Unconditional" : Kind) : DisplayType;
}

public sealed class LdfNodeAttr
{
    public string Name { get; set; } = "";
    public string Protocol { get; set; } = "";
    public int ConfiguredNad { get; set; }
    public int InitialNad { get; set; }
    public bool HasInitialNad { get; set; }
    public string SupplierId { get; set; } = "";
    public string FunctionId { get; set; } = "";
    public string VariantId { get; set; } = "";
    public string ResponseError { get; set; } = "";
    public List<string> FaultStateSignals { get; } = new();
    public double P2MinMs { get; set; }
    public double StMinMs { get; set; }
    public double NAsTimeoutMs { get; set; }
    public double NCrTimeoutMs { get; set; }
    public List<string> ConfigurableFrames { get; } = new();
}

public sealed class LdfEncodingValue
{
    public string Kind { get; set; } = ""; /* logical / physical / bcd / ascii / float */
    public long Value { get; set; }
    public long MinValue { get; set; }
    public long MaxValue { get; set; }
    public double Scale { get; set; } = 1;
    public double Offset { get; set; }
    public string Text { get; set; } = "";
}

public sealed class LdfSignalGroup
{
    public string Name { get; set; } = "";
    public int SizeBits { get; set; }
    public List<(string Signal, int Offset)> Members { get; } = new();
}

public sealed class LdfCompositeConfig
{
    public string Name { get; set; } = "";
    public List<(string LogicalNode, List<string> CompositionNodes)> Items { get; } = new();
}

public sealed class LdfEncoding
{
    public string Name { get; set; } = "";
    public List<LdfEncodingValue> Values { get; } = new();
}

public sealed class LdfRepresentation
{
    public string Encoding { get; set; } = "";
    public List<string> Signals { get; } = new();
}
