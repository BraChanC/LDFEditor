namespace LdfEditor.Models;

/// <summary>Parsed Node Capability File (intermediate, before merge into LDF).</summary>
public sealed class NcfFile
{
    public string LanguageVersion { get; set; } = "";
    public string FileRevision { get; set; } = "";
    public bool IsMotorolaByteOrder { get; set; }
    public List<NcfNode> Nodes { get; } = new();
}

public sealed class NcfNode
{
    public string Name { get; set; } = "";
    public string ProtocolVersion { get; set; } = "";
    public long Supplier { get; set; }
    public long Function { get; set; }
    public long Variant { get; set; }
    public double? BitrateKbps { get; set; }
    public string SendsWakeUp { get; set; } = "";
    public double? ResponseTolerancePct { get; set; }
    public int? WakeupTimeMs { get; set; }
    public int? PoweronTimeMs { get; set; }

    public int? Nad { get; set; }
    public int? NadTo { get; set; }
    public List<int> NadList { get; } = new();
    public int? DiagnosticClass { get; set; }
    public double? P2MinMs { get; set; }
    public double? StMinMs { get; set; }
    public double? NAsTimeoutMs { get; set; }
    public double? NCrTimeoutMs { get; set; }
    public List<int> SupportSids { get; } = new();

    public List<NcfFrame> Frames { get; } = new();
    public List<NcfEncoding> Encodings { get; } = new();
    public string ResponseError { get; set; } = "";
    public string ResetIndication { get; set; } = "";
    public List<string> FaultStateSignals { get; } = new();
}

public sealed class NcfFrame
{
    public string Name { get; set; } = "";
    public bool IsPublish { get; set; }
    public int? MessageId { get; set; }
    public int? FrameId { get; set; }
    public int Length { get; set; } = 8;
    public string EventTriggeredFrame { get; set; } = "";
    public List<NcfSignal> Signals { get; } = new();
}

public sealed class NcfSignal
{
    public string Name { get; set; } = "";
    public string InitValue { get; set; } = "0";
    public int SizeBits { get; set; } = 1;
    public int Offset { get; set; }
    public string EncodingRef { get; set; } = "";
    public NcfEncoding? InlineEncoding { get; set; }
}

public sealed class NcfEncoding
{
    public string Name { get; set; } = "";
    public List<LdfEncodingValue> Values { get; } = new();
}
