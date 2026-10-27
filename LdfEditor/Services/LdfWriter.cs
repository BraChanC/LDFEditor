using System.Globalization;
using System.Text;
using LdfEditor.Models;

namespace LdfEditor.Services;

/// <summary>
/// Serialize LdfDatabase to LDF text.
/// Section order matches Vector VLDFParser / LIN 2.x:
/// Nodes → Signals → Diagnostic_signals → Frames → Sporadic_frames →
/// Event_triggered_frames → Diagnostic_frames → Node_attributes →
/// Schedule_tables → Signal_encoding_types → Signal_representation
/// </summary>
public static class LdfWriter
{
    public static string Write(LdfDatabase db)
    {
        var sb = new StringBuilder();
        sb.AppendLine("LIN_description_file;");
        sb.AppendLine($"LIN_protocol_version = \"{Esc(db.ProtocolVersion)}\";");
        sb.AppendLine($"LIN_language_version = \"{Esc(db.LanguageVersion)}\";");
        if (!string.IsNullOrEmpty(db.FileRevision))
            sb.AppendLine($"LDF_file_revision = \"{Esc(db.FileRevision)}\";");
        sb.AppendLine($"LIN_speed = {Fmt(db.SpeedKbps)} kbps;");
        if (!string.IsNullOrEmpty(db.ChannelName))
            sb.AppendLine($"Channel_name = {QuoteIdentOrString(db.ChannelName)};");
        if (db.IsMotorolaByteOrder)
            sb.AppendLine("LIN_sig_byte_order_big_endian;");
        sb.AppendLine();

        WriteNodes(sb, db);
        WriteSignals(sb, db);
        WriteDiagnosticSignals(sb, db);
        WriteUnconditionalFrames(sb, db);
        WriteSporadic(sb, db);
        WriteEventTriggered(sb, db);
        WriteDiagnosticFrames(sb, db);
        WriteNodeAttributes(sb, db);
        WriteSchedules(sb, db);
        WriteEncodings(sb, db);
        WriteRepresentations(sb, db);
        return sb.ToString();
    }

    private static void WriteNodes(StringBuilder sb, LdfDatabase db)
    {
        /* Vector WriteNodes_J2602_2021 uses Commander / Responders tags. */
        bool j2602_2021 = IsJ2602_2021(db.ProtocolVersion);
        var masterTag = j2602_2021 ? "Commander" : "Master";
        var slavesTag = j2602_2021 ? "Responders" : "Slaves";
        sb.AppendLine("Nodes {");
        sb.AppendLine($"  {masterTag}: {db.Master}, {Fmt(db.TimebaseMs)} ms, {Fmt(db.JitterMs)} ms;");
        if (db.Slaves.Count > 0)
            sb.AppendLine($"  {slavesTag}: {string.Join(", ", db.Slaves)};");
        sb.AppendLine("}");
        sb.AppendLine();

        /* Vector WriteLDF_13 / OEM → Diagnostic_addresses instead of Node_attributes. */
        if (db.DiagnosticAddresses.Count > 0)
        {
            sb.AppendLine("Diagnostic_addresses {");
            foreach (var kv in db.DiagnosticAddresses)
                sb.AppendLine($"  {kv.Key}: {kv.Value};");
            sb.AppendLine("}");
            sb.AppendLine();
        }
    }

    private static bool IsJ2602_2021(string protocol) =>
        protocol.Contains("J2602_1_2", StringComparison.OrdinalIgnoreCase)
        || protocol.Contains("2602_1_2", StringComparison.OrdinalIgnoreCase);

    private static void WriteSignals(StringBuilder sb, LdfDatabase db)
    {
        if (db.Signals.Count == 0) return;
        sb.AppendLine("Signals {");
        foreach (var s in db.Signals)
        {
            var subs = s.Subscribers.Count > 0 ? ", " + string.Join(", ", s.Subscribers) : "";
            sb.AppendLine($"  {s.Name}: {s.SizeBits}, {s.InitValue}, {s.Publisher}{subs};");
        }
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void WriteDiagnosticSignals(StringBuilder sb, LdfDatabase db)
    {
        if (db.DiagnosticSignals.Count == 0) return;
        sb.AppendLine("Diagnostic_signals {");
        foreach (var s in db.DiagnosticSignals)
            sb.AppendLine($"  {s.Name}: {s.SizeBits}, {s.InitValue};");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static bool IsDiagnosticFrame(LdfFrame f) =>
        f.IsDiagnostic
        || f.Name is "MasterReq" or "SlaveResp" or "CommanderReq" or "ResponderResp"
        || f.Id is 0x3C or 0x3D;

    private static void WriteUnconditionalFrames(StringBuilder sb, LdfDatabase db)
    {
        var frames = db.Frames.Where(f => !IsDiagnosticFrame(f)).ToList();
        if (frames.Count == 0) return;
        sb.AppendLine("Frames {");
        foreach (var f in frames)
        {
            if (string.IsNullOrWhiteSpace(f.Publisher))
                continue; /* invalid for Frames {}; skip rather than emit ", ," */
            sb.AppendLine($"  {f.Name}: {FmtId(f.Id)}, {f.Publisher}, {f.Length} {{");
            foreach (var fs in f.Signals)
                sb.AppendLine($"    {fs.Name}, {fs.StartBit};");
            sb.AppendLine("  }");
        }
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void WriteSporadic(StringBuilder sb, LdfDatabase db)
    {
        if (db.SporadicFrames.Count == 0) return;
        sb.AppendLine("Sporadic_frames {");
        foreach (var sp in db.SporadicFrames)
            sb.AppendLine($"  {sp.Name}: {string.Join(", ", sp.FrameNames)};");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void WriteEventTriggered(StringBuilder sb, LdfDatabase db)
    {
        if (db.EventTriggeredFrames.Count == 0) return;
        sb.AppendLine("Event_triggered_frames {");
        foreach (var et in db.EventTriggeredFrames)
        {
            var frames = et.FrameNames.Count > 0 ? ", " + string.Join(", ", et.FrameNames) : "";
            sb.AppendLine($"  {et.Name}: {et.CollisionResolvingSchedule}, {FmtId(et.Id)}{frames};");
        }
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void WriteDiagnosticFrames(StringBuilder sb, LdfDatabase db)
    {
        var diags = db.Frames.Where(IsDiagnosticFrame).ToList();
        if (diags.Count == 0) return;

        sb.AppendLine("Diagnostic_frames {");
        foreach (var f in diags.OrderBy(f => f.Id == 0x3C ? 0 : f.Id == 0x3D ? 1 : f.Id + 2))
        {
            /* Vector / LIN: Diagnostic_frames has no publisher field. */
            var id = f.Name is "MasterReq" or "CommanderReq" ? 0x3C
                : f.Name is "SlaveResp" or "ResponderResp" ? 0x3D
                : f.Id;
            sb.AppendLine($"  {f.Name}: {FmtId(id)} {{");
            foreach (var fs in f.Signals)
                sb.AppendLine($"    {fs.Name}, {fs.StartBit};");
            sb.AppendLine("  }");
        }
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void WriteNodeAttributes(StringBuilder sb, LdfDatabase db)
    {
        if (db.NodeAttributes.Count == 0) return;
        sb.AppendLine("Node_attributes {");
        foreach (var a in db.NodeAttributes)
        {
            sb.AppendLine($"  {a.Name} {{");
            if (!string.IsNullOrEmpty(a.Protocol))
            {
                /* Vector expects quoted protocol, e.g. LIN_protocol = "2.2"; */
                sb.AppendLine($"    LIN_protocol = \"{Esc(a.Protocol.Trim())}\";");
            }
            sb.AppendLine($"    configured_NAD = {FmtNad(a.ConfiguredNad)};");
            if (a.HasInitialNad)
                sb.AppendLine($"    initial_NAD = {FmtNad(a.InitialNad)};");
            if (!string.IsNullOrEmpty(a.SupplierId) || !string.IsNullOrEmpty(a.FunctionId))
            {
                var supplier = string.IsNullOrEmpty(a.SupplierId) ? "0" : a.SupplierId;
                var function = string.IsNullOrEmpty(a.FunctionId) ? "0" : a.FunctionId;
                if (!string.IsNullOrEmpty(a.VariantId))
                    sb.AppendLine($"    product_id = {supplier}, {function}, {a.VariantId};");
                else
                    sb.AppendLine($"    product_id = {supplier}, {function};");
            }
            if (!string.IsNullOrEmpty(a.ResponseError))
                sb.AppendLine($"    response_error = {a.ResponseError};");
            if (a.FaultStateSignals.Count > 0)
                sb.AppendLine($"    fault_state_signals = {string.Join(", ", a.FaultStateSignals)};");
            if (a.P2MinMs > 0)
                sb.AppendLine($"    P2_min = {Fmt(a.P2MinMs)} ms;");
            if (a.StMinMs > 0 || a.P2MinMs > 0)
                sb.AppendLine($"    ST_min = {Fmt(a.StMinMs)} ms;");
            if (a.NAsTimeoutMs > 0)
                sb.AppendLine($"    N_As_timeout = {Fmt(a.NAsTimeoutMs)} ms;");
            if (a.NCrTimeoutMs > 0)
                sb.AppendLine($"    N_Cr_timeout = {Fmt(a.NCrTimeoutMs)} ms;");
            if (a.ConfigurableFrames.Count > 0)
            {
                sb.AppendLine("    configurable_frames {");
                foreach (var cf in a.ConfigurableFrames)
                    sb.AppendLine($"      {cf};");
                sb.AppendLine("    }");
            }
            sb.AppendLine("  }");
        }
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void WriteSchedules(StringBuilder sb, LdfDatabase db)
    {
        sb.AppendLine("Schedule_tables {");
        foreach (var t in db.Schedules)
        {
            sb.AppendLine($"  {t.Name} {{");
            foreach (var slot in t.Slots)
                sb.AppendLine($"    {FormatScheduleSlot(slot)} delay {Fmt(slot.DelayMs)} ms;");
            sb.AppendLine("  }");
        }
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static string FormatScheduleSlot(LdfScheduleSlot e)
    {
        switch (e.Kind)
        {
            case "AssignNAD":
                return $"AssignNAD {{{e.NodeName}}}";
            case "ConditionalChangeNAD":
                return $"ConditionalChangeNAD {{{string.Join(", ", e.IntArgs)}}}";
            case "DataDump":
                return $"DataDump {{{e.NodeName}, {string.Join(", ", e.IntArgs)}}}";
            case "SaveConfiguration":
                return $"SaveConfiguration {{{e.NodeName}}}";
            case "AssignFrameIdRange":
            {
                var body = e.NodeName + ", " + e.StartIndex;
                if (e.HasExplicitPids && e.Pids.Count > 0)
                    body += ", " + string.Join(", ", e.Pids);
                return $"AssignFrameIdRange {{{body}}}";
            }
            case "AssignFrameId":
                return $"AssignFrameId {{{e.NodeName}, {e.AssignedFrame}}}";
            case "UnassignFrameId":
                return $"UnassignFrameId {{{e.NodeName}, {e.AssignedFrame}}}";
            case "FreeFormat":
                return $"FreeFormat {{{string.Join(", ", e.IntArgs)}}}";
            case "ReadByIdentifier":
            {
                var args = e.IntArgs.Count > 0
                    ? ", " + string.Join(", ", e.IntArgs)
                    : "";
                return $"ReadByIdentifier {{{e.NodeName}{args}}}";
            }
            case "MasterReq":
                return "MasterReq";
            case "SlaveResp":
                return "SlaveResp";
            default:
                if (!string.IsNullOrEmpty(e.FrameName))
                    return e.FrameName;
                if (!string.IsNullOrEmpty(e.Command))
                    return LdfPropertyBuilder.StripScheduleCommand(e.Command);
                return e.Kind;
        }
    }

    private static void WriteEncodings(StringBuilder sb, LdfDatabase db)
    {
        if (db.Encodings.Count == 0) return;
        sb.AppendLine("Signal_encoding_types {");
        foreach (var enc in db.Encodings)
        {
            sb.AppendLine($"  {enc.Name} {{");
            foreach (var v in enc.Values)
            {
                switch (v.Kind)
                {
                    case "logical":
                        if (string.IsNullOrEmpty(v.Text))
                            sb.AppendLine($"    logical_value, {v.Value};");
                        else
                            sb.AppendLine($"    logical_value, {v.Value}, \"{Esc(v.Text)}\";");
                        break;
                    case "physical":
                        if (string.IsNullOrEmpty(v.Text))
                            sb.AppendLine(
                                $"    physical_value, {v.MinValue}, {v.MaxValue}, {Fmt(v.Scale)}, {Fmt(v.Offset)};");
                        else
                            sb.AppendLine(
                                $"    physical_value, {v.MinValue}, {v.MaxValue}, {Fmt(v.Scale)}, {Fmt(v.Offset)}, \"{Esc(v.Text)}\";");
                        break;
                    case "ascii":
                        sb.AppendLine("    ascii_value;");
                        break;
                    case "bcd":
                        sb.AppendLine("    bcd_value;");
                        break;
                    case "float":
                        sb.AppendLine("    float_value;");
                        break;
                }
            }
            sb.AppendLine("  }");
        }
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void WriteRepresentations(StringBuilder sb, LdfDatabase db)
    {
        if (db.Representations.Count == 0) return;
        sb.AppendLine("Signal_representation {");
        foreach (var rep in db.Representations)
        {
            if (rep.Signals.Count == 0) continue;
            sb.AppendLine($"  {rep.Encoding}: {string.Join(", ", rep.Signals)};");
        }
        sb.AppendLine("}");
    }

    private static string Esc(string s) => (s ?? "").Replace("\"", "\\\"");
    private static string Fmt(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static string FmtId(int id) => id <= 9 ? id.ToString(CultureInfo.InvariantCulture) : $"0x{id:X}";
    private static string FmtNad(int nad) => nad <= 9 ? nad.ToString(CultureInfo.InvariantCulture) : $"0x{nad:X}";

    private static string QuoteIdentOrString(string name)
    {
        if (string.IsNullOrEmpty(name)) return "\"\"";
        /* Bare identifier if legal; else quoted string. */
        if (char.IsLetter(name[0]) || name[0] == '_')
        {
            if (name.All(c => char.IsLetterOrDigit(c) || c == '_'))
                return name;
        }
        return $"\"{Esc(name)}\"";
    }
}
