using System.Globalization;
using System.IO;
using Antlr4.Runtime;
using LdfEditor.Antlr;
using LdfEditor.Models;
using LdfEditor.Services.Diagnostics;

namespace LdfEditor.Services;

public sealed class LdfAntlrLoader
{
    public LdfDatabase Load(string path)
    {
        var text = File.ReadAllText(path);
        return LoadText(text, path);
    }

    public LdfDatabase LoadText(string text, string path)
    {
        var result = LdfDiagnosticEngine.Analyze(text, path);
        if (result.HasErrors || result.Database == null)
            throw new LdfParseException(
                result.FormatErrorDialog(),
                result.AsLegacyIssues(),
                result.Diagnostics);

        result.Database.FilePath = path ?? "";
        return result.Database;
    }

    /// <summary>Parse model only (diagnostics already collected by <see cref="LdfDiagnosticEngine"/>).</summary>
    internal LdfDatabase LoadTextUnchecked(string text, string path)
    {
        var input = new AntlrInputStream(text);
        var lexer = new LdfLexer(input);
        lexer.RemoveErrorListeners();
        var tokens = new CommonTokenStream(lexer);
        var parser = new LdfParser(tokens);
        parser.RemoveErrorListeners();
        /* Silent: engine already validated; Bail on unexpected failure */
        parser.ErrorHandler = new BailErrorStrategy();
        var tree = parser.ldfFile();
        var displayName = string.IsNullOrEmpty(path)
            ? "Untitled"
            : Path.GetFileNameWithoutExtension(path);
        var v = new LdfModelVisitor(displayName);
        v.Visit(tree);
        v.Database.FilePath = path ?? "";
        if (string.IsNullOrEmpty(v.Database.Name))
            v.Database.Name = displayName;
        EnsureStandardDiagnosticFrames(v.Database);
        return v.Database;
    }

    private static void EnsureStandardDiagnosticFrames(LdfDatabase db)
    {
        /* Prefer Vector / J2602_2021 Commander/Responder names when already present. */
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

    private static string FormatParseIssues(IReadOnlyList<LdfSectionOrderValidator.Issue> issues)
    {
        if (issues.Count == 0) return "The file couldn't be loaded!";
        var first = issues[0];
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("The file couldn't be loaded!");
        sb.AppendLine();
        sb.AppendLine($"Error in line {first.Line}: {first.Message}");
        if (!string.IsNullOrEmpty(first.Why))
        {
            sb.AppendLine();
            sb.AppendLine($"Why: {first.Why}");
        }
        if (!string.IsNullOrEmpty(first.Constraint))
        {
            sb.AppendLine();
            sb.AppendLine($"Rule: {first.Constraint}");
        }
        sb.AppendLine();
        sb.Append("Error: General Parser failure.");
        return sb.ToString();
    }

    private sealed class ThrowingErrorListener : BaseErrorListener
    {
        public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e)
        {
            var token = offendingSymbol?.Text ?? "";
            var suggestion = LdfUnknownKeywordScanner.Closest(token);
            var why = suggestion == null
                ? $"Parser could not accept token '{token}' at this position. {msg}"
                : $"Parser could not accept token '{token}'. Did you mean '{suggestion}'? ({msg})";
            var message = suggestion == null
                ? $"Keyword expected on reading token: {token}"
                : $"Keyword expected on reading token: {token} (did you mean {suggestion}?)";
            var issue = new LdfSectionOrderValidator.Issue(
                Line: line,
                Token: token,
                Message: message,
                PreviousSection: "",
                Constraint: LdfUnknownKeywordScanner.RuleId,
                Why: why,
                RuleId: LdfUnknownKeywordScanner.RuleId);
            throw new LdfParseException(FormatParseIssues(new[] { issue }), new[] { issue });
        }
    }

    private sealed class LdfModelVisitor : LdfBaseVisitor<object?>
    {
        public LdfDatabase Database { get; } = new();
        public LdfModelVisitor(string name) => Database.Name = name;

        public override object? VisitProtocolVersion(LdfParser.ProtocolVersionContext context)
        {
            Database.ProtocolVersion = Unquote(context.STRING()?.GetText());
            return null;
        }

        public override object? VisitLanguageVersion(LdfParser.LanguageVersionContext context)
        {
            Database.LanguageVersion = Unquote(context.STRING()?.GetText());
            return null;
        }

        public override object? VisitFileRevision(LdfParser.FileRevisionContext context)
        {
            Database.FileRevision = Unquote(context.STRING()?.GetText());
            return null;
        }

        public override object? VisitSpeed(LdfParser.SpeedContext context)
        {
            Database.SpeedKbps = ParseDouble(context.number().GetText());
            return null;
        }

        public override object? VisitChannelName(LdfParser.ChannelNameContext context)
        {
            /* Channel_name is bus metadata only; tree root keeps LDF file name. */
            Database.ChannelName = context.identifier()?.GetText()
                ?? Unquote(context.STRING()?.GetText());
            if (string.IsNullOrEmpty(Database.ChannelPostfix))
                Database.ChannelPostfix = Database.ChannelName;
            return null;
        }

        public override object? VisitDiagnosticAddressItem(LdfParser.DiagnosticAddressItemContext context)
        {
            var node = context.identifier().GetText();
            var nad = ParseInt(context.INT().GetText());
            Database.DiagnosticAddresses[node] = nad;
            var existing = Database.NodeAttributes.FirstOrDefault(a => a.Name == node);
            if (existing != null)
                existing.ConfiguredNad = nad;
            else
            {
                Database.NodeAttributes.Add(new LdfNodeAttr
                {
                    Name = node,
                    ConfiguredNad = nad,
                    Protocol = Database.ProtocolVersion
                });
            }
            return null;
        }

        public override object? VisitNodes(LdfParser.NodesContext context)
        {
            var ids = context.identifier();
            var nums = context.number();
            if (ids.Length > 0) Database.Master = ids[0].GetText();
            if (nums.Length > 0) Database.TimebaseMs = ParseDouble(nums[0].GetText());
            if (nums.Length > 1) Database.JitterMs = ParseDouble(nums[1].GetText());
            /* J2602: Master: name, tb ms, jitter ms, header bits, tolerance % */
            if (nums.Length > 2)
                Database.J2602HeaderLength = (uint)ParseLong(nums[2].GetText());
            if (nums.Length > 3)
                Database.J2602MasterResponseTolerance = ParseDouble(nums[3].GetText());
            for (int i = 1; i < ids.Length; i++)
                Database.Slaves.Add(ids[i].GetText());
            return null;
        }

        public override object? VisitSigByteOrder(LdfParser.SigByteOrderContext context)
        {
            var t = context.GetText();
            Database.IsMotorolaByteOrder = t.Contains("big_endian", StringComparison.Ordinal);
            return null;
        }

        public override object? VisitSignalGroup(LdfParser.SignalGroupContext context)
        {
            var g = new LdfSignalGroup
            {
                Name = context.identifier().GetText(),
                SizeBits = ParseInt(context.INT().GetText())
            };
            foreach (var item in context.signalGroupItem())
            {
                g.Members.Add((item.identifier().GetText(), ParseInt(item.INT().GetText())));
            }
            Database.SignalGroups.Add(g);
            return null;
        }

        public override object? VisitConfiguration(LdfParser.ConfigurationContext context)
        {
            var cfg = new LdfCompositeConfig { Name = context.identifier().GetText() };
            foreach (var item in context.configurationItem())
            {
                var ids = item.identifier();
                if (ids.Length == 0) continue;
                var logical = ids[0].GetText();
                var nodes = ids.Skip(1).Select(i => i.GetText()).ToList();
                cfg.Items.Add((logical, nodes));
            }
            Database.CompositeConfigs.Add(cfg);
            return null;
        }

        public override object? VisitSignalItem(LdfParser.SignalItemContext context)
        {
            var ids = context.identifier();
            var sig = new LdfSignal
            {
                Name = ids[0].GetText(),
                SizeBits = ParseInt(context.INT().GetText()),
                InitValue = context.initValue()?.GetText() ?? "0",
                Publisher = ids.Length > 1 ? ids[1].GetText() : ""
            };
            for (int i = 2; i < ids.Length; i++)
                sig.Subscribers.Add(ids[i].GetText());
            Database.Signals.Add(sig);
            return null;
        }

        public override object? VisitDiagnosticSignalItem(LdfParser.DiagnosticSignalItemContext context)
        {
            var ints = context.INT();
            Database.DiagnosticSignals.Add(new LdfSignal
            {
                Name = context.identifier().GetText(),
                SizeBits = ints.Length > 0 ? ParseInt(ints[0].GetText()) : 8,
                InitValue = ints.Length > 1 ? ints[1].GetText() : "0"
            });
            return null;
        }

        public override object? VisitFrameItem(LdfParser.FrameItemContext context)
        {
            var ids = context.identifier();
            var ints = context.INT();
            var frameName = ids[0].GetText();
            var frameId = ParseInt(ints[0].GetText());
            bool isDiag = frameName is "MasterReq" or "SlaveResp" or "CommanderReq" or "ResponderResp"
                || frameId is 0x3C or 0x3D;
            var frame = new LdfFrame
            {
                Name = frameName,
                Id = frameId,
                Publisher = ids.Length > 1 ? ids[1].GetText() : "",
                Length = ints.Length > 1 ? ParseInt(ints[1].GetText()) : 0,
                Kind = isDiag ? "Diagnostic" : "Unconditional"
            };
            int maxEnd = 0;
            foreach (var fs in context.frameSignal())
            {
                var name = fs.identifier().GetText();
                var start = ParseInt(fs.INT().GetText());
                frame.Signals.Add(new LdfFrameSignal { Name = name, StartBit = start });
                var bits = Database.FindSignal(name)?.SizeBits ?? 1;
                maxEnd = Math.Max(maxEnd, start + bits);
            }
            if (frame.Length <= 0 && maxEnd > 0)
                frame.Length = (maxEnd + 7) / 8;
            if (frame.Length <= 0) frame.Length = 8;
            Database.Frames.Add(frame);
            return null;
        }

        public override object? VisitSporadicFrameItem(LdfParser.SporadicFrameItemContext context)
        {
            var ids = context.identifier();
            var sf = new LdfSporadicFrame { Name = ids[0].GetText() };
            for (int i = 1; i < ids.Length; i++)
                sf.FrameNames.Add(ids[i].GetText());
            Database.SporadicFrames.Add(sf);
            return null;
        }

        public override object? VisitEventTriggeredFrameItem(LdfParser.EventTriggeredFrameItemContext context)
        {
            var ids = context.identifier();
            var et = new LdfEventTriggeredFrame
            {
                Name = ids[0].GetText(),
                CollisionResolvingSchedule = ids.Length > 1 ? ids[1].GetText() : "",
                Id = ParseInt(context.INT().GetText())
            };
            for (int i = 2; i < ids.Length; i++)
                et.FrameNames.Add(ids[i].GetText());
            Database.EventTriggeredFrames.Add(et);
            return null;
        }

        public override object? VisitDiagnosticFrames(LdfParser.DiagnosticFramesContext context)
        {
            /* Vector J2602:2021 uses CommanderReq / ResponderResp. */
            var diagText = context.GetText();
            var reqName = diagText.Contains("CommanderReq", StringComparison.Ordinal) ? "CommanderReq" : "MasterReq";
            var respName = diagText.Contains("ResponderResp", StringComparison.Ordinal) ? "ResponderResp" : "SlaveResp";
            var master = new LdfFrame
            {
                Name = reqName,
                Id = ParseInt(context.masterId.Text),
                Length = 8,
                Kind = "Diagnostic",
                Publisher = Database.Master
            };
            foreach (var fs in context._masterSigs)
                master.Signals.Add(new LdfFrameSignal
                {
                    Name = fs.identifier().GetText(),
                    StartBit = ParseInt(fs.INT().GetText())
                });
            var slave = new LdfFrame
            {
                Name = respName,
                Id = ParseInt(context.slaveId.Text),
                Length = 8,
                Kind = "Diagnostic",
                Publisher = Database.Slaves.FirstOrDefault() ?? ""
            };
            foreach (var fs in context._slaveSigs)
                slave.Signals.Add(new LdfFrameSignal
                {
                    Name = fs.identifier().GetText(),
                    StartBit = ParseInt(fs.INT().GetText())
                });
            Database.Frames.RemoveAll(f => f.IsDiagnostic && (
                f.Name is "MasterReq" or "SlaveResp" or "CommanderReq" or "ResponderResp"));
            Database.Frames.Add(master);
            Database.Frames.Add(slave);
            return null;
        }

        public override object? VisitNodeAttribute(LdfParser.NodeAttributeContext context)
        {
            var ints = context.INT();
            var protoCtx = context.protocolValue();
            var protocol = protoCtx?.STRING() != null
                ? Unquote(protoCtx.STRING().GetText())
                : protoCtx?.number()?.GetText() ?? "";
            var attr = new LdfNodeAttr
            {
                Name = context.identifier().GetText(),
                Protocol = protocol,
                ConfiguredNad = ints.Length > 0 ? ParseInt(ints[0].GetText()) : 0
            };
            if (ints.Length > 1)
            {
                attr.InitialNad = ParseInt(ints[1].GetText());
                attr.HasInitialNad = true;
            }
            foreach (var body in context.nodeAttributeBody())
                ApplyNodeBody(attr, body);
            Database.NodeAttributes.Add(attr);
            return null;
        }

        private static void ApplyNodeBody(LdfNodeAttr na, LdfParser.NodeAttributeBodyContext body)
        {
            var t = body.GetText();
            if (t.StartsWith("product_id", StringComparison.Ordinal))
            {
                var pi = body.INT();
                if (pi != null && pi.Length >= 1) na.SupplierId = pi[0].GetText();
                if (pi != null && pi.Length >= 2) na.FunctionId = pi[1].GetText();
                if (pi != null && pi.Length >= 3) na.VariantId = pi[2].GetText();
            }
            else if (t.StartsWith("response_error", StringComparison.Ordinal))
            {
                var ids = body.identifier();
                if (ids.Length > 0) na.ResponseError = ids[0].GetText();
            }
            else if (t.StartsWith("fault_state_signals", StringComparison.Ordinal))
            {
                foreach (var id in body.identifier())
                    na.FaultStateSignals.Add(id.GetText());
            }
            else if (t.StartsWith("P2_min", StringComparison.Ordinal))
                na.P2MinMs = ParseDouble(body.number().GetText());
            else if (t.StartsWith("ST_min", StringComparison.Ordinal))
                na.StMinMs = ParseDouble(body.number().GetText());
            else if (t.StartsWith("N_As_timeout", StringComparison.Ordinal))
                na.NAsTimeoutMs = ParseDouble(body.number().GetText());
            else if (t.StartsWith("N_Cr_timeout", StringComparison.Ordinal))
                na.NCrTimeoutMs = ParseDouble(body.number().GetText());
            else if (body.configurableFrames() != null)
            {
                foreach (var cf in body.configurableFrames().configurableFrame())
                    na.ConfigurableFrames.Add(cf.identifier().GetText());
            }
        }

        public override object? VisitScheduleTable(LdfParser.ScheduleTableContext context)
        {
            var table = new LdfScheduleTable { Name = context.identifier().GetText() };
            foreach (var cmd in context.scheduleCommand())
            {
                var slot = new LdfScheduleSlot
                {
                    DelayMs = ParseDouble(cmd.number().GetText())
                };
                var sc = cmd.scheduleCmd();
                if (sc != null)
                {
                    slot.Command = sc.GetText();
                    FillScheduleCmd(slot, sc);
                }
                table.Slots.Add(slot);
            }
            Database.Schedules.Add(table);
            return null;
        }

        private static void FillScheduleCmd(LdfScheduleSlot e, LdfParser.ScheduleCmdContext sc)
        {
            var text = sc.GetText();
            var ids = sc.identifier();
            var ints = sc.INT();
            bool named = true;
            if (text.StartsWith("AssignNAD", StringComparison.Ordinal))
            {
                e.Kind = "AssignNAD";
                if (ids.Length > 0) e.NodeName = ids[0].GetText();
                e.FrameName = "MasterReq";
            }
            else if (text.StartsWith("ConditionalChangeNAD", StringComparison.Ordinal))
            {
                e.Kind = "ConditionalChangeNAD";
                e.FrameName = "MasterReq";
                foreach (var t in ints) e.IntArgs.Add(ParseInt(t.GetText()));
            }
            else if (text.StartsWith("DataDump", StringComparison.Ordinal))
            {
                e.Kind = "DataDump";
                if (ids.Length > 0) e.NodeName = ids[0].GetText();
                e.FrameName = "MasterReq";
                foreach (var t in ints) e.IntArgs.Add(ParseInt(t.GetText()));
            }
            else if (text.StartsWith("SaveConfiguration", StringComparison.Ordinal))
            {
                e.Kind = "SaveConfiguration";
                if (ids.Length > 0) e.NodeName = ids[0].GetText();
                e.FrameName = "MasterReq";
            }
            else if (text.StartsWith("AssignFrameIdRange", StringComparison.Ordinal))
            {
                e.Kind = "AssignFrameIdRange";
                if (ids.Length > 0) e.NodeName = ids[0].GetText();
                e.FrameName = "MasterReq";
                if (ints.Length > 0) e.StartIndex = ParseInt(ints[0].GetText());
                for (int i = 1; i < ints.Length; i++)
                {
                    e.Pids.Add(ParseInt(ints[i].GetText()));
                    e.HasExplicitPids = true;
                }
            }
            else if (text.StartsWith("UnassignFrameId", StringComparison.Ordinal))
            {
                e.Kind = "UnassignFrameId";
                if (ids.Length > 0) e.NodeName = ids[0].GetText();
                if (ids.Length > 1) e.AssignedFrame = ids[1].GetText();
                e.FrameName = "MasterReq";
            }
            else if (text.StartsWith("AssignFrameId", StringComparison.Ordinal))
            {
                e.Kind = "AssignFrameId";
                if (ids.Length > 0) e.NodeName = ids[0].GetText();
                if (ids.Length > 1) e.AssignedFrame = ids[1].GetText();
                e.FrameName = "MasterReq";
            }
            else if (text.StartsWith("FreeFormat", StringComparison.Ordinal))
            {
                e.Kind = "FreeFormat";
                e.FrameName = "MasterReq";
                foreach (var t in ints) e.IntArgs.Add(ParseInt(t.GetText()));
            }
            else if (text.StartsWith("MasterReq", StringComparison.Ordinal))
            {
                e.Kind = "MasterReq";
                e.FrameName = "MasterReq";
            }
            else if (text.StartsWith("SlaveResp", StringComparison.Ordinal))
            {
                e.Kind = "SlaveResp";
                e.FrameName = "SlaveResp";
            }
            else if (ids.Length > 0)
            {
                e.Kind = ids[0].GetText();
                e.FrameName = e.Kind;
                named = false;
            }
            else
            {
                e.Kind = e.Command;
                e.FrameName = e.Command;
                named = false;
            }
            if (named) e.DisplayType = PrettyCmdType(e.Kind);
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
            _ => kind
        };

        public override object? VisitSignalEncoding(LdfParser.SignalEncodingContext context)
        {
            var enc = new LdfEncoding { Name = context.identifier().GetText() };
            foreach (var v in context.encodingValue())
            {
                var ev = new LdfEncodingValue();
                var t = v.GetText();
                var ints = v.INT();
                var nums = v.number();
                if (t.StartsWith("logical_value", StringComparison.Ordinal))
                {
                    ev.Kind = "logical";
                    if (ints.Length > 0) ev.Value = ParseLong(ints[0].GetText());
                    if (v.STRING() != null) ev.Text = Unquote(v.STRING().GetText());
                }
                else if (t.StartsWith("physical_value", StringComparison.Ordinal))
                {
                    ev.Kind = "physical";
                    if (ints.Length > 0) ev.MinValue = ParseLong(ints[0].GetText());
                    if (ints.Length > 1) ev.MaxValue = ParseLong(ints[1].GetText());
                    if (nums.Length > 0) ev.Scale = ParseDouble(nums[0].GetText());
                    if (nums.Length > 1) ev.Offset = ParseDouble(nums[1].GetText());
                    if (v.STRING() != null) ev.Text = Unquote(v.STRING().GetText());
                }
                else if (t.StartsWith("bcd_value", StringComparison.Ordinal))
                    ev.Kind = "bcd";
                else if (t.StartsWith("ascii_value", StringComparison.Ordinal))
                    ev.Kind = "ascii";
                else if (t.StartsWith("float_value", StringComparison.Ordinal))
                    ev.Kind = "float";
                enc.Values.Add(ev);
            }
            Database.Encodings.Add(enc);
            return null;
        }

        public override object? VisitSignalRepresentationItem(LdfParser.SignalRepresentationItemContext context)
        {
            var ids = context.identifier();
            var rep = new LdfRepresentation { Encoding = ids[0].GetText() };
            for (int i = 1; i < ids.Length; i++)
                rep.Signals.Add(ids[i].GetText());
            Database.Representations.Add(rep);
            return null;
        }

        private static string Unquote(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') return s[1..^1];
            return s;
        }

        private static double ParseDouble(string s) =>
            double.Parse(s, CultureInfo.InvariantCulture);

        private static int ParseInt(string s)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToInt32(s, 16);
            return int.Parse(s, CultureInfo.InvariantCulture);
        }

        private static long ParseLong(string s)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToInt64(s, 16);
            return long.Parse(s, CultureInfo.InvariantCulture);
        }
    }
}

/// <summary>Vector Read Error / General Parser failure.</summary>
public sealed class LdfParseException : Exception
{
    public IReadOnlyList<LdfSectionOrderValidator.Issue> Issues { get; }
    public IReadOnlyList<LdfDiagnostic> Diagnostics { get; }

    public LdfParseException(
        string message,
        IReadOnlyList<LdfSectionOrderValidator.Issue> issues,
        IReadOnlyList<LdfDiagnostic>? diagnostics = null)
        : base(message)
    {
        Issues = issues;
        Diagnostics = diagnostics ?? Array.Empty<LdfDiagnostic>();
    }
}
