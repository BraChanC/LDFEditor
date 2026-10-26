using System.Globalization;
using System.Text;
using LdfEditor.Models;
using LdfEditor.ViewModels;

namespace LdfEditor.Services;

/// <summary>Ports LdfViewPro Fill*Properties into grouped property trees.</summary>
public static class LdfPropertyBuilder
{
    public static void FillCommonLinBus(LdfDatabase db, IList<PropNode> dest)
    {
        dest.Clear();
        var common = PropNode.Category("Common");
        common.Add("Protocol Version", db.ProtocolVersion);
        common.Add("Language Version", db.LanguageVersion);
        if (!string.IsNullOrEmpty(db.FileRevision))
            common.Add("File Revision", db.FileRevision);
        dest.Add(common);

        var bus = PropNode.Category("LIN Bus Properties");
        var name = string.IsNullOrEmpty(db.ChannelName) ? db.Name : db.ChannelName;
        bus.Add("Name", name);
        bus.Add("Baudrate", $"{db.SpeedKbps:0.000} kbps");
        bus.Add("Channel Postfix",
            string.IsNullOrEmpty(db.ChannelPostfix) ? name : db.ChannelPostfix);
        bus.Add("Comment", db.Comment ?? "");
        bus.Add("Byte Order", db.IsMotorolaByteOrder ? "Motorola" : "Intel");
        if (db.J2602HeaderLength.HasValue)
            bus.Add("J2602 Header Length", $"{db.J2602HeaderLength.Value} bits");
        if (db.J2602MasterResponseTolerance.HasValue)
            bus.Add("J2602 Master Response Tolerance",
                $"{db.J2602MasterResponseTolerance.Value:0.###} %");
        dest.Add(bus);
    }

    public static void FillFrame(LdfDatabase db, LdfFrame f, IList<PropNode> dest)
    {
        dest.Clear();
        var cat = PropNode.Category("Frame Properties");
        cat.Add("Name", f.Name);
        var idText = LdfDisplayFormat.FormatId(f.Id);

        bool isMasterReq = f.IsDiagnostic && (f.Name == "MasterReq" || f.Id == 0x3C);
        bool isSlaveResp = f.IsDiagnostic && (f.Name == "SlaveResp" || f.Id == 0x3D);
        if (isMasterReq || isSlaveResp)
        {
            cat.Add("Frame ID", idText);
            cat.Add("Size", (f.Length > 0 ? f.Length : 8).ToString());
            var commanderSpaced = string.IsNullOrEmpty(db.Master)
                ? "Commander node"
                : $"{db.Master} (Commander node)";
            var commanderTight = string.IsNullOrEmpty(db.Master)
                ? "Commander node"
                : $"{db.Master}(Commander node)";
            if (isMasterReq)
            {
                cat.Add("Publisher", commanderSpaced);
                cat.Add("Subscribers", "Each responder node");
            }
            else
            {
                cat.Add("Publisher", "Each responder node");
                cat.Add("Subscribers", commanderTight);
            }
            cat.Add("CS Model", "Classic");
            dest.Add(cat);
            return;
        }

        cat.Add("ID", idText);
        cat.Add("Length [Byte]", f.Length.ToString());
        cat.Add("Publisher", f.Publisher);
        cat.Add("Type", f.IsDiagnostic ? "Diagnostic" : "Unconditional");
        dest.Add(cat);
    }

    public static void FillSignal(LdfDatabase db, LdfSignal s, IList<PropNode> dest,
        int? startBit = null, string? frameName = null)
    {
        dest.Clear();
        dest.Add(BuildEncodingCategory(db.FindEncodingForSignal(s.Name)));
        var cat = PropNode.Category("Signal Properties");
        cat.Add("Name", s.Name);
        cat.Add("Length", s.SizeBits.ToString());
        cat.Add("Init Value", s.InitValue);
        cat.Add("Publisher", s.Publisher);
        cat.Add("Subscribers", string.Join(", ", s.Subscribers));
        if (startBit.HasValue)
            cat.Add("Startbit", startBit.Value.ToString());
        if (!string.IsNullOrEmpty(frameName))
            cat.Add("Frame", frameName);
        cat.Add("Type", "Scalar");
        cat.Add("Unit", "");
        dest.Add(cat);
    }

    private static PropNode BuildEncodingCategory(LdfEncoding? enc)
    {
        var cat = PropNode.Category("Signal Encodings");
        if (enc == null)
        {
            cat.Add("Name", "");
            cat.Add("Physical", "");
            cat.Add("Logical", "");
            return cat;
        }
        cat.Add("Name", enc.Name);
        var physical = new StringBuilder();
        var logical = new StringBuilder();
        foreach (var v in enc.Values)
        {
            if (v.Kind == "logical")
            {
                if (logical.Length > 0) logical.Append(", ");
                logical.AppendFormat(CultureInfo.InvariantCulture, "{0}: {1}", v.Value, v.Text);
            }
            else if (v.Kind == "physical")
            {
                if (physical.Length > 0) physical.Append("; ");
                physical.Append(string.IsNullOrEmpty(v.Text)
                    ? string.Format(CultureInfo.InvariantCulture,
                        "{0}..{1}, factor={2:G}, offset={3:G}", v.MinValue, v.MaxValue, v.Scale, v.Offset)
                    : string.Format(CultureInfo.InvariantCulture,
                        "{0}..{1}, factor={2:G}, offset={3:G}, \"{4}\"", v.MinValue, v.MaxValue, v.Scale, v.Offset, v.Text));
            }
            else if (v.Kind == "bcd")
            {
                if (physical.Length > 0) physical.Append("; ");
                physical.Append("BCD");
            }
            else if (v.Kind == "ascii")
            {
                if (physical.Length > 0) physical.Append("; ");
                physical.Append("ASCII");
            }
        }
        cat.Add("Physical", physical.ToString());
        cat.Add("Logical", logical.ToString());
        return cat;
    }

    public static void FillEventTriggered(LdfEventTriggeredFrame e, IList<PropNode> dest)
    {
        dest.Clear();
        var cat = PropNode.Category("Event-Triggered Frame");
        cat.Add("Name", e.Name);
        cat.Add("ID", LdfDisplayFormat.FormatId(e.Id));
        cat.Add("Schedule", e.CollisionResolvingSchedule);
        if (e.FrameNames.Count > 0)
            cat.Add("Associated Frames", string.Join(", ", e.FrameNames));
        dest.Add(cat);
    }

    public static void FillSporadic(LdfSporadicFrame s, IList<PropNode> dest)
    {
        dest.Clear();
        var cat = PropNode.Category("Sporadic Frame");
        cat.Add("Name", s.Name);
        if (s.FrameNames.Count > 0)
            cat.Add("Associated Frames", string.Join(", ", s.FrameNames));
        dest.Add(cat);
    }

    public static void FillCommand(string command, IList<PropNode> dest)
    {
        dest.Clear();
        var cat = PropNode.Category("Predefined Command");
        cat.Add("Command", command);
        dest.Add(cat);
    }

    public static void FillNodeNameOnly(string name, IList<PropNode> dest)
    {
        dest.Clear();
        var cat = PropNode.Category("Node Properties");
        cat.Add("Name", name);
        dest.Add(cat);
    }

    public static void FillNodeFromRow(NodeRow row, IList<PropNode> dest)
    {
        dest.Clear();
        var cat = PropNode.Category("Node Properties");
        cat.Add("Name", row.Node);
        cat.Add("Configured NAD", row.ConfiguredNad);
        cat.Add("Initial NAD", row.InitialNad);
        cat.Add("Protocol", row.Protocol);
        cat.Add("Supplier ID", row.SupplierId);
        cat.Add("Function ID", row.FunctionId);
        cat.Add("Variant ID", row.VariantId);
        dest.Add(cat);
    }

    public static void FillGroup(string title, IList<PropNode> dest)
    {
        dest.Clear();
        var cat = PropNode.Category("Properties");
        cat.Add("Group", title);
        dest.Add(cat);
    }

    public static void FillScheduleTable(LdfDatabase db, int scheduleIndex, IList<PropNode> dest)
    {
        dest.Clear();
        if (scheduleIndex < 0 || scheduleIndex >= db.Schedules.Count) return;
        var st = db.Schedules[scheduleIndex];
        var busLoad = CalcScheduleBusLoadPercent(db, scheduleIndex, out var cycleMs);
        var cat = PropNode.Category("Schedule Table Properties");
        cat.Add("Name", st.Name);
        cat.Add("Table Id", scheduleIndex.ToString());
        cat.Add("Bus Load [%]", busLoad.ToString("0.0", CultureInfo.InvariantCulture));
        cat.Add("Total Cycle Time [ms]", cycleMs.ToString("0.0", CultureInfo.InvariantCulture));
        cat.Add("Resolves collisions for ETF(s)", FormatEtfsResolvedBySchedule(db, st.Name));
        dest.Add(cat);
    }

    public static void FillScheduleSlot(LdfDatabase db, int scheduleIndex, int entryIndex, IList<PropNode> dest)
    {
        dest.Clear();
        if (scheduleIndex < 0 || scheduleIndex >= db.Schedules.Count) return;
        var st = db.Schedules[scheduleIndex];
        if (entryIndex < 0 || entryIndex >= st.Slots.Count) return;
        var e = st.Slots[entryIndex];
        int nbytes = ScheduleEntryPayloadBytes(db, e);
        double tMin = CalcLinFrameTimeMs(db, nbytes);
        double tMax = CalcLinFrameTimeMaxMs(db, nbytes);
        CalcSlotPeriodMinMax(db, scheduleIndex, entryIndex, out var pMin, out var pMax);

        var general = PropNode.Category("General Slot Properties");
        general.Add("Delay", e.DelayMs.ToString("0.00", CultureInfo.InvariantCulture));
        dest.Add(general);

        var assigned = PropNode.Category("Assigned Frame / Command");
        var fname = string.IsNullOrEmpty(e.FrameName) ? StripScheduleCommand(e.Command) : e.FrameName;
        assigned.Add("Frame", fname);
        if (!string.IsNullOrEmpty(e.DisplayType))
            assigned.Add("Command", e.DisplayType);
        assigned.Add("Min Frame Time", tMin.ToString("0.00", CultureInfo.InvariantCulture));
        assigned.Add("Max Frame Time", tMax.ToString("0.00", CultureInfo.InvariantCulture));
        assigned.Add("Min Max Period", $"{pMin:0.00} / {pMax:0.00}");
        dest.Add(assigned);

        FillSpecificSlotProperties(db, e, dest);
    }

    private static void FillSpecificSlotProperties(LdfDatabase db, LdfScheduleSlot e, IList<PropNode> dest)
    {
        if (e.Kind is "AssignNAD" or "SaveConfiguration")
        {
            if (string.IsNullOrEmpty(e.NodeName)) return;
            var cat = PropNode.Category("Specific Slot Properties");
            cat.Add("Node", e.NodeName);
            dest.Add(cat);
            return;
        }
        if (e.Kind is "AssignFrameId" or "UnassignFrameId")
        {
            var cat = PropNode.Category("Specific Slot Properties");
            cat.Add("Node", e.NodeName);
            cat.Add("Frame", e.AssignedFrame);
            dest.Add(cat);
            return;
        }
        if (e.Kind == "AssignFrameIdRange")
        {
            var cat = PropNode.Category("Specific Slot Properties");
            cat.Add("Node", e.NodeName);
            cat.Add("Start Index", LdfDisplayFormat.FormatByte(e.StartIndex < 0 ? 0 : e.StartIndex));
            var pids = ResolveAssignFrameIdRangePids(db, e);
            cat.Add("PIDs", string.Join(", ", pids.Select(LdfDisplayFormat.FormatByte)));
            dest.Add(cat);
            return;
        }
        if (e.Kind == "DataDump")
        {
            var cat = PropNode.Category("Specific Slot Properties");
            cat.Add("Node", e.NodeName);
            for (int i = 0; i < e.IntArgs.Count && i < 5; i++)
                cat.Add($"D{i}", LdfDisplayFormat.FormatByte(e.IntArgs[i]));
            dest.Add(cat);
            return;
        }
        if (e.Kind == "ConditionalChangeNAD")
        {
            var cat = PropNode.Category("Specific Slot Properties");
            string[] keys = ["NAD", "Id", "Byte", "Mask", "Inv", "New NAD"];
            for (int i = 0; i < e.IntArgs.Count && i < 6; i++)
                cat.Add(keys[i], LdfDisplayFormat.FormatByte(e.IntArgs[i]));
            dest.Add(cat);
            return;
        }
        if (e.Kind == "FreeFormat")
        {
            var cat = PropNode.Category("Specific Slot Properties");
            for (int i = 0; i < e.IntArgs.Count && i < 8; i++)
                cat.Add($"D{i}", LdfDisplayFormat.FormatByte(e.IntArgs[i]));
            dest.Add(cat);
        }
    }

    public static string StripScheduleCommand(string cmd)
    {
        var brace = cmd.IndexOf('{');
        var key = brace >= 0 ? cmd[..brace] : cmd;
        return key.TrimEnd();
    }

    public static int ScheduleEntryPayloadBytes(LdfDatabase db, LdfScheduleSlot e)
    {
        var key = string.IsNullOrEmpty(e.FrameName) ? StripScheduleCommand(e.Command) : e.FrameName;
        var fr = db.FindFrame(key);
        if (fr != null) return fr.Length > 0 ? fr.Length : 0;

        var et = db.EventTriggeredFrames.FirstOrDefault(x => x.Name == key);
        if (et != null)
            return et.FrameNames.Select(n => db.FindFrame(n)?.Length ?? 0).DefaultIfEmpty(0).Max();

        var sp = db.SporadicFrames.FirstOrDefault(x => x.Name == key);
        if (sp != null)
            return sp.FrameNames.Select(n => db.FindFrame(n)?.Length ?? 0).DefaultIfEmpty(0).Max();

        if (key is "MasterReq" or "SlaveResp" ||
            e.Kind is "AssignNAD" or "ConditionalChangeNAD" or "DataDump" or "SaveConfiguration"
                or "AssignFrameIdRange" or "AssignFrameId" or "UnassignFrameId" or "FreeFormat")
            return 8;
        return 0;
    }

    public static double CalcLinFrameTimeMs(LdfDatabase db, int payloadBytes)
    {
        if (db.SpeedKbps <= 0 || payloadBytes < 0) return 0;
        var baud = db.SpeedKbps * 1000.0;
        var bits = 34.0 + 10.0 * (payloadBytes + 1);
        return bits * 1000.0 / baud;
    }

    public static double CalcLinFrameTimeMaxMs(LdfDatabase db, int payloadBytes) =>
        CalcLinFrameTimeMs(db, payloadBytes) * 1.4;

    public static double CalcScheduleBusLoadPercent(LdfDatabase db, int scheduleIndex, out double totalCycleMs)
    {
        totalCycleMs = 0;
        if (scheduleIndex < 0 || scheduleIndex >= db.Schedules.Count) return 0;
        var st = db.Schedules[scheduleIndex];
        double busy = 0;
        foreach (var e in st.Slots)
        {
            totalCycleMs += e.DelayMs;
            busy += CalcLinFrameTimeMaxMs(db, ScheduleEntryPayloadBytes(db, e));
        }
        return totalCycleMs <= 0 ? 0 : busy * 100.0 / totalCycleMs;
    }

    public static void CalcSlotPeriodMinMax(LdfDatabase db, int scheduleIndex, int entryIndex,
        out double minPeriod, out double maxPeriod)
    {
        minPeriod = 0;
        maxPeriod = 0;
        if (scheduleIndex < 0 || scheduleIndex >= db.Schedules.Count) return;
        var st = db.Schedules[scheduleIndex];
        if (entryIndex < 0 || entryIndex >= st.Slots.Count || st.Slots.Count == 0) return;
        var key = st.Slots[entryIndex].Command;
        var hits = new List<int>();
        for (int i = 0; i < st.Slots.Count; i++)
            if (st.Slots[i].Command == key) hits.Add(i);

        double PeriodBetween(int from, int to)
        {
            double p = 0;
            int n = st.Slots.Count;
            int i = from;
            do
            {
                p += st.Slots[i].DelayMs;
                i = (i + 1) % n;
            } while (i != to);
            return p;
        }

        if (hits.Count <= 1)
        {
            double cycle = st.Slots.Sum(s => s.DelayMs);
            minPeriod = cycle;
            maxPeriod = cycle;
            return;
        }
        var periods = new List<double>();
        for (int h = 0; h < hits.Count; h++)
        {
            int next = hits[(h + 1) % hits.Count];
            periods.Add(PeriodBetween(hits[h], next));
        }
        minPeriod = periods.Min();
        maxPeriod = periods.Max();
    }

    private static string FormatEtfsResolvedBySchedule(LdfDatabase db, string scheduleName)
    {
        var names = db.EventTriggeredFrames
            .Where(e => e.CollisionResolvingSchedule == scheduleName)
            .Select(e => e.Name)
            .ToList();
        return names.Count == 0 ? "-" : string.Join(", ", names);
    }

    private static List<int> ResolveAssignFrameIdRangePids(LdfDatabase db, LdfScheduleSlot e)
    {
        if (e.HasExplicitPids && e.Pids.Count > 0)
            return e.Pids.ToList();
        var na = db.NodeAttributes.FirstOrDefault(a => a.Name == e.NodeName);
        var outPids = new List<int>();
        if (na == null || e.StartIndex < 0) return outPids;
        for (int k = 0; k < 4; k++)
        {
            int idx = e.StartIndex + k;
            if (idx < 0 || idx >= na.ConfigurableFrames.Count) break;
            outPids.Add(LinProtectedId(LookupFrameOrEventId(db, na.ConfigurableFrames[idx])));
        }
        return outPids;
    }

    private static int LookupFrameOrEventId(LdfDatabase db, string name)
    {
        var f = db.FindFrame(name);
        if (f != null) return f.Id;
        var et = db.EventTriggeredFrames.FirstOrDefault(e => e.Name == name);
        return et?.Id ?? 0;
    }

    private static int LinProtectedId(int frameId)
    {
        uint id = (uint)frameId & 0x3Fu;
        uint p0 = ((id >> 0) ^ (id >> 1) ^ (id >> 2) ^ (id >> 4)) & 1u;
        uint p1 = (~((id >> 1) ^ (id >> 3) ^ (id >> 4) ^ (id >> 5))) & 1u;
        return (int)(id | (p0 << 6) | (p1 << 7));
    }
}
