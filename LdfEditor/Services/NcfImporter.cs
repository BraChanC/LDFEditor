using System.Globalization;
using System.IO;
using System.Text;
using LdfEditor.Models;

namespace LdfEditor.Services;

/// <summary>
/// Merge parsed NCF into an open LDF model (Vector ModelReader.AddNCFToModel).
/// </summary>
public static class NcfImporter
{
    public sealed class ImportResult
    {
        public int NodesAdded { get; set; }
        public int FramesAdded { get; set; }
        public int SignalsAdded { get; set; }
        public int EncodingsAdded { get; set; }
        public List<string> Warnings { get; } = new();
        public string Summary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Imported {NodesAdded} slave node(s), {FramesAdded} frame(s), {SignalsAdded} signal(s), {EncodingsAdded} encoding(s).");
            foreach (var w in Warnings)
                sb.AppendLine("Warning: " + w);
            return sb.ToString().TrimEnd();
        }
    }

    public static ImportResult Merge(LdfDatabase db, NcfFile ncf)
    {
        var result = new ImportResult();
        if (ncf.Nodes.Count == 0)
        {
            result.Warnings.Add("NCF contains no node definitions.");
            return result;
        }

        if (ncf.IsMotorolaByteOrder)
            db.IsMotorolaByteOrder = true;

        foreach (var node in ncf.Nodes)
            MergeNode(db, node, result);

        return result;
    }

    private static void MergeNode(LdfDatabase db, NcfNode node, ImportResult result)
    {
        if (!string.IsNullOrEmpty(node.ProtocolVersion)
            && !string.IsNullOrEmpty(db.ProtocolVersion)
            && !ProtocolCompatible(db.ProtocolVersion, node.ProtocolVersion))
        {
            throw new InvalidDataException(
                $"LIN protocol version of NCF slave node does not fit version of LDF ({node.ProtocolVersion} vs {db.ProtocolVersion})");
        }

        var slaveName = UniqueName(node.Name, n =>
            string.Equals(db.Master, n, StringComparison.OrdinalIgnoreCase)
            || db.Slaves.Any(s => string.Equals(s, n, StringComparison.OrdinalIgnoreCase)));
        if (!string.Equals(slaveName, node.Name, StringComparison.Ordinal))
            result.Warnings.Add($"Slave renamed '{node.Name}' → '{slaveName}' (name conflict).");

        db.Slaves.Add(slaveName);
        result.NodesAdded++;

        var attr = new LdfNodeAttr
        {
            Name = slaveName,
            Protocol = string.IsNullOrEmpty(node.ProtocolVersion) ? db.ProtocolVersion : node.ProtocolVersion,
            ConfiguredNad = node.Nad ?? 1,
            InitialNad = node.Nad ?? 1,
            HasInitialNad = node.Nad.HasValue,
            SupplierId = $"0x{node.Supplier:X}",
            FunctionId = $"0x{node.Function:X}",
            VariantId = node.Variant.ToString(CultureInfo.InvariantCulture),
            ResponseError = node.ResponseError,
            P2MinMs = node.P2MinMs ?? 0,
            StMinMs = node.StMinMs ?? 0,
            NAsTimeoutMs = node.NAsTimeoutMs ?? 0,
            NCrTimeoutMs = node.NCrTimeoutMs ?? 0
        };
        foreach (var fs in node.FaultStateSignals)
            attr.FaultStateSignals.Add(fs);
        db.NodeAttributes.Add(attr);

        /* Encodings first (LIN 2.1+ block). */
        var encNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var enc in node.Encodings)
        {
            var encName = UniqueName(enc.Name, n => db.Encodings.Any(e => e.Name == n));
            encNameMap[enc.Name] = encName;
            var ldfEnc = new LdfEncoding { Name = encName };
            foreach (var v in enc.Values)
                ldfEnc.Values.Add(CloneEncodingValue(v));
            db.Encodings.Add(ldfEnc);
            result.EncodingsAdded++;
        }

        var master = string.IsNullOrEmpty(db.Master) ? "Master" : db.Master;

        foreach (var fr in node.Frames)
        {
            var frameName = UniqueName(fr.Name, n =>
                db.Frames.Any(f => f.Name == n)
                || db.EventTriggeredFrames.Any(e => e.Name == n)
                || db.SporadicFrames.Any(s => s.Name == n));
            if (!string.Equals(frameName, fr.Name, StringComparison.Ordinal))
                result.Warnings.Add($"Frame renamed '{fr.Name}' → '{frameName}'.");

            int id = fr.FrameId ?? fr.MessageId ?? NextFreeFrameId(db);
            if (db.Frames.Any(f => f.Id == id) || db.EventTriggeredFrames.Any(e => e.Id == id))
                id = NextFreeFrameId(db);

            var publisher = fr.IsPublish ? slaveName : master;
            var frame = new LdfFrame
            {
                Name = frameName,
                Id = id,
                Publisher = publisher,
                Length = fr.Length > 0 ? fr.Length : 8,
                Kind = "Unconditional"
            };

            foreach (var sig in fr.Signals)
            {
                var sigName = UniqueName(sig.Name, n => db.FindSignal(n) != null);
                if (!string.Equals(sigName, sig.Name, StringComparison.Ordinal))
                    result.Warnings.Add($"Signal renamed '{sig.Name}' → '{sigName}'.");

                var ldfSig = new LdfSignal
                {
                    Name = sigName,
                    SizeBits = sig.SizeBits > 0 ? sig.SizeBits : 1,
                    InitValue = string.IsNullOrEmpty(sig.InitValue) ? "0" : sig.InitValue,
                    Publisher = fr.IsPublish ? slaveName : master
                };
                if (fr.IsPublish)
                {
                    if (!ldfSig.Subscribers.Contains(master))
                        ldfSig.Subscribers.Add(master);
                }
                else
                {
                    if (!ldfSig.Subscribers.Contains(slaveName))
                        ldfSig.Subscribers.Add(slaveName);
                }

                db.Signals.Add(ldfSig);
                result.SignalsAdded++;
                frame.Signals.Add(new LdfFrameSignal { Name = sigName, StartBit = sig.Offset });

                /* Encoding link */
                string? encRef = null;
                if (sig.InlineEncoding != null)
                {
                    var encName = UniqueName(sig.InlineEncoding.Name,
                        n => db.Encodings.Any(e => e.Name == n));
                    var ldfEnc = new LdfEncoding { Name = encName };
                    foreach (var v in sig.InlineEncoding.Values)
                        ldfEnc.Values.Add(CloneEncodingValue(v));
                    db.Encodings.Add(ldfEnc);
                    result.EncodingsAdded++;
                    encRef = encName;
                }
                else if (!string.IsNullOrEmpty(sig.EncodingRef))
                {
                    encRef = encNameMap.TryGetValue(sig.EncodingRef, out var mapped)
                        ? mapped : sig.EncodingRef;
                }

                if (!string.IsNullOrEmpty(encRef) && db.Encodings.Any(e => e.Name == encRef))
                {
                    var rep = db.Representations.FirstOrDefault(r => r.Encoding == encRef);
                    if (rep == null)
                    {
                        rep = new LdfRepresentation { Encoding = encRef };
                        db.Representations.Add(rep);
                    }
                    if (!rep.Signals.Contains(sigName))
                        rep.Signals.Add(sigName);
                }

                /* response_error name remap if signal was renamed */
                if (!string.IsNullOrEmpty(attr.ResponseError)
                    && string.Equals(attr.ResponseError, sig.Name, StringComparison.OrdinalIgnoreCase))
                    attr.ResponseError = sigName;
            }

            if (!string.IsNullOrEmpty(fr.EventTriggeredFrame))
            {
                var etName = UniqueName(fr.EventTriggeredFrame, n =>
                    db.EventTriggeredFrames.Any(e => e.Name == n)
                    || db.Frames.Any(f => f.Name == n));
                if (!db.EventTriggeredFrames.Any(e => e.Name == etName))
                {
                    db.EventTriggeredFrames.Add(new LdfEventTriggeredFrame
                    {
                        Name = etName,
                        Id = NextFreeFrameId(db),
                        CollisionResolvingSchedule = db.Schedules.FirstOrDefault()?.Name ?? ""
                    });
                }
                var et = db.EventTriggeredFrames.First(e => e.Name == etName);
                if (!et.FrameNames.Contains(frameName))
                    et.FrameNames.Add(frameName);
            }

            if (!attr.ConfigurableFrames.Contains(frameName))
                attr.ConfigurableFrames.Add(frameName);

            db.Frames.Add(frame);
            result.FramesAdded++;
        }
    }

    private static bool ProtocolCompatible(string ldf, string ncf)
    {
        static string Norm(string v) => v.Trim().Trim('"').ToUpperInvariant();
        var a = Norm(ldf);
        var b = Norm(ncf);
        if (a == b) return true;
        static bool IsoOr22(string x) =>
            x is "2.2" or "ISO17987" or "ISO17987:2015"
            || x.Contains("ISO17987", StringComparison.Ordinal);
        static bool V21(string x) => x == "2.1" || IsoOr22(x);
        static bool V20(string x) => x == "2.0" || V21(x);
        static bool J2602(string x) => x.Contains("J2602", StringComparison.Ordinal);
        if (J2602(a) && J2602(b)) return true;
        if (IsoOr22(a) && IsoOr22(b)) return true;
        if (a == "2.1" && V21(b)) return true;
        if (a == "2.0" && V20(b)) return true;
        if (a == "1.3" && b == "1.3") return true;
        var alen = Math.Min(3, a.Length);
        var blen = Math.Min(3, b.Length);
        return (blen > 0 && a.StartsWith(b[..blen], StringComparison.Ordinal))
               || (alen > 0 && b.StartsWith(a[..alen], StringComparison.Ordinal));
    }

    private static LdfEncodingValue CloneEncodingValue(LdfEncodingValue v) => new()
    {
        Kind = v.Kind,
        Value = v.Value,
        MinValue = v.MinValue,
        MaxValue = v.MaxValue,
        Scale = v.Scale,
        Offset = v.Offset,
        Text = v.Text
    };

    private static string UniqueName(string pattern, Func<string, bool> exists)
    {
        if (!exists(pattern)) return pattern;
        for (int i = 1; i < 10000; i++)
        {
            var n = $"{pattern}_{i}";
            if (!exists(n)) return n;
        }
        return pattern + "_" + Guid.NewGuid().ToString("N")[..4];
    }

    private static int NextFreeFrameId(LdfDatabase db)
    {
        var used = db.Frames.Select(f => f.Id)
            .Concat(db.EventTriggeredFrames.Select(e => e.Id))
            .ToHashSet();
        for (int id = 0; id < 64; id++)
            if (!used.Contains(id)) return id;
        return 0;
    }
}
