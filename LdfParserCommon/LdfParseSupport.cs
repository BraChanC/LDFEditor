using System.Globalization;
using LdfEditor.Models;
using LdfEditor.Services.Diagnostics;

namespace LdfEditor.Services.Parsing;

public static class LdfParseSupport
{
    public static double ParseDouble(string s) =>
        double.Parse(s, CultureInfo.InvariantCulture);

    public static int ParseInt(string s)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToInt32(s, 16);
        return int.Parse(s, CultureInfo.InvariantCulture);
    }

    public static long ParseLong(string s)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToInt64(s, 16);
        return long.Parse(s, CultureInfo.InvariantCulture);
    }

    public static void EnsureStandardDiagnosticFrames(LdfDatabase db)
    {
        bool hasCommander = db.Frames.Any(f =>
            f.Name is "CommanderReq" or "ResponderResp");
        if (hasCommander) return;

        void EnsureDiag(string name, int id, string prefix, string publisher)
        {
            var existing = db.Frames.FirstOrDefault(f => f.Name == name);
            if (existing != null)
            {
                existing.Kind = "Diagnostic";
                return;
            }
            var fr = new LdfFrame
            {
                Name = name,
                Id = id,
                Length = 8,
                Kind = "Diagnostic",
                Publisher = publisher
            };
            for (int i = 0; i < 8; i++)
            {
                var sigName = $"{prefix}B{i}";
                fr.Signals.Add(new LdfFrameSignal { Name = sigName, StartBit = i * 8 });
                if (db.FindSignal(sigName) == null)
                {
                    db.DiagnosticSignals.Add(new LdfSignal
                    {
                        Name = sigName,
                        SizeBits = 8,
                        InitValue = "0",
                        Publisher = publisher
                    });
                }
            }
            db.Frames.Add(fr);
        }
        EnsureDiag("MasterReq", 0x3C, "MasterReq", db.Master);
        var slavePub = db.Slaves.FirstOrDefault() ?? "";
        EnsureDiag("SlaveResp", 0x3D, "SlaveResp", slavePub);
    }

    public static void FillScheduleSlot(LdfScheduleSlot e, string name)
    {
        e.Kind = name;
        switch (name)
        {
            case "AssignNAD":
            case "ConditionalChangeNAD":
            case "DataDump":
            case "SaveConfiguration":
            case "AssignFrameIdRange":
            case "AssignFrameId":
            case "UnassignFrameId":
            case "FreeFormat":
                e.FrameName = "MasterReq";
                break;
            case "MasterReq":
            case "CommanderReq":
            case "SlaveResp":
            case "ResponderResp":
                e.FrameName = name;
                break;
            default:
                e.FrameName = name;
                e.DisplayType = "";
                return;
        }
        e.DisplayType = PrettyCmdType(name);
    }

    private static string PrettyCmdType(string kind) => kind switch
    {
        "AssignNAD" => "Assign NAD",
        "ConditionalChangeNAD" => "Conditional Change NAD",
        "DataDump" => "Data Dump",
        "SaveConfiguration" => "Save Configuration",
        "AssignFrameIdRange" => "Assign Frame-ID Range",
        "AssignFrameId" => "Assign Frame-ID",
        "UnassignFrameId" => "Unassign Frame-ID",
        "FreeFormat" => "Free Format",
        "MasterReq" => "Master Request",
        "SlaveResp" => "Slave Response",
        "CommanderReq" => "Master Request",
        "ResponderResp" => "Slave Response",
        _ => kind
    };
}
