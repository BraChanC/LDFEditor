using System.Globalization;
using LdfEditor.Models;
using LdfEditor.Services; /* LdfParseException, LdfSectionOrderValidator */

namespace LdfEditor.Services.Parsing;

/// <summary>
/// Shared LDF model builder from a token stream (used by Pidgin / Sprache / Superpower front-ends).
/// </summary>
public static class LdfTokenModelParser
{
    public static LdfDatabase Parse(IReadOnlyList<LdfToken> tokens, string displayName)
    {
        var p = new Parser(tokens, displayName);
        p.ParseFile();
        LdfParseSupport.EnsureStandardDiagnosticFrames(p.Db);
        return p.Db;
    }

    private sealed class Parser
    {
        private readonly IReadOnlyList<LdfToken> _toks;
        private int _i;
        public LdfDatabase Db { get; }

        public Parser(IReadOnlyList<LdfToken> toks, string displayName)
        {
            _toks = toks;
            Db = new LdfDatabase { Name = displayName };
        }

        private LdfToken Cur => _i < _toks.Count ? _toks[_i] : new LdfToken(LdfTokKind.Eof, "", 0, 0);
        private bool AtEof => Cur.Kind == LdfTokKind.Eof;

        private LdfToken Take()
        {
            var t = Cur;
            if (_i < _toks.Count) _i++;
            return t;
        }

        private bool Look(LdfTokKind k) => Cur.Kind == k;
        private bool LookIdent(string s) =>
            Cur.Kind is LdfTokKind.Ident or LdfTokKind.Kw &&
            string.Equals(Cur.Text, s, StringComparison.Ordinal);

        private void Expect(LdfTokKind k, string? what = null)
        {
            if (Cur.Kind != k)
                throw Fail($"expected {what ?? k.ToString()}, got {Cur}");
            Take();
        }

        private string ExpectIdent()
        {
            if (Cur.Kind is not (LdfTokKind.Ident or LdfTokKind.Kw))
                throw Fail($"expected identifier, got {Cur}");
            return Take().Text;
        }

        private void ExpectKw(string kw)
        {
            if (!LookIdent(kw)) throw Fail($"expected '{kw}', got {Cur}");
            Take();
        }

        private Exception Fail(string msg) =>
            new LdfParseException(
                $"Parse error at line {Cur.Line}, col {Cur.Column}: {msg}",
                Array.Empty<LdfSectionOrderValidator.Issue>());

        public void ParseFile()
        {
            ExpectKw("LIN_description_file");
            Expect(LdfTokKind.Semi, "';");
            while (!AtEof)
                ParseSection();
        }

        private void ParseSection()
        {
            if (LookIdent("LIN_protocol_version")) { Take(); Expect(LdfTokKind.Eq); Db.ProtocolVersion = ExpectString(); Expect(LdfTokKind.Semi); return; }
            if (LookIdent("LIN_language_version")) { Take(); Expect(LdfTokKind.Eq); Db.LanguageVersion = ExpectString(); Expect(LdfTokKind.Semi); return; }
            if (LookIdent("LDF_file_revision")) { Take(); Expect(LdfTokKind.Eq); Db.FileRevision = ExpectString(); Expect(LdfTokKind.Semi); return; }
            if (LookIdent("LIN_speed")) { Take(); Expect(LdfTokKind.Eq); Db.SpeedKbps = ExpectNumber(); ExpectKw("kbps"); Expect(LdfTokKind.Semi); return; }
            if (LookIdent("Channel_name"))
            {
                Take(); Expect(LdfTokKind.Eq);
                Db.ChannelName = Look(LdfTokKind.String) ? ExpectString() : ExpectIdent();
                if (string.IsNullOrEmpty(Db.ChannelPostfix)) Db.ChannelPostfix = Db.ChannelName;
                Expect(LdfTokKind.Semi); return;
            }
            if (LookIdent("LIN_sig_byte_order_big_endian")) { Take(); Db.IsMotorolaByteOrder = true; Expect(LdfTokKind.Semi); return; }
            if (LookIdent("LIN_sig_byte_order_little_endian")) { Take(); Db.IsMotorolaByteOrder = false; Expect(LdfTokKind.Semi); return; }
            if (LookIdent("Nodes")) { ParseNodes(); return; }
            if (LookIdent("composite")) { ParseComposite(); return; }
            if (LookIdent("Diagnostic_addresses")) { ParseDiagnosticAddresses(); return; }
            if (LookIdent("Signals")) { ParseSignals(); return; }
            if (LookIdent("Diagnostic_signals")) { ParseDiagnosticSignals(); return; }
            if (LookIdent("Frames")) { ParseFrames(); return; }
            if (LookIdent("Sporadic_frames")) { ParseSporadic(); return; }
            if (LookIdent("Event_triggered_frames")) { ParseEventTriggered(); return; }
            if (LookIdent("Diagnostic_frames")) { ParseDiagnosticFrames(); return; }
            if (LookIdent("Node_attributes")) { ParseNodeAttributes(); return; }
            if (LookIdent("Schedule_tables")) { ParseSchedules(); return; }
            if (LookIdent("Signal_groups")) { ParseSignalGroups(); return; }
            if (LookIdent("Signal_encoding_types")) { ParseEncodings(); return; }
            if (LookIdent("Signal_representation")) { ParseRepresentations(); return; }
            throw Fail($"unknown section keyword '{Cur.Text}'");
        }

        private string ExpectString()
        {
            if (!Look(LdfTokKind.String)) throw Fail($"expected string, got {Cur}");
            return Take().Text;
        }

        private double ExpectNumber()
        {
            bool neg = false;
            if (Look(LdfTokKind.Minus)) { Take(); neg = true; }
            if (Cur.Kind is not (LdfTokKind.Int or LdfTokKind.Float))
                throw Fail($"expected number, got {Cur}");
            var v = LdfParseSupport.ParseDouble(Take().Text);
            return neg ? -v : v;
        }

        private int ExpectInt()
        {
            bool neg = false;
            if (Look(LdfTokKind.Minus)) { Take(); neg = true; }
            if (Cur.Kind != LdfTokKind.Int) throw Fail($"expected int, got {Cur}");
            var v = LdfParseSupport.ParseInt(Take().Text);
            return neg ? -v : v;
        }

        private long ExpectLong()
        {
            bool neg = false;
            if (Look(LdfTokKind.Minus)) { Take(); neg = true; }
            if (Cur.Kind is not (LdfTokKind.Int or LdfTokKind.Float))
                throw Fail($"expected int, got {Cur}");
            var v = LdfParseSupport.ParseLong(Take().Text);
            return neg ? -v : v;
        }

        private void ParseNodes()
        {
            Take(); Expect(LdfTokKind.LBrace);
            if (!(LookIdent("Master") || LookIdent("Commander"))) throw Fail("Master/Commander expected");
            Take();
            Expect(LdfTokKind.Colon);
            Db.Master = ExpectIdent();
            Expect(LdfTokKind.Comma);
            Db.TimebaseMs = ExpectNumber();
            ExpectKw("ms");
            Expect(LdfTokKind.Comma);
            Db.JitterMs = ExpectNumber();
            ExpectKw("ms");
            if (Look(LdfTokKind.Comma))
            {
                Take();
                Db.J2602HeaderLength = (uint)ExpectNumber();
                ExpectKw("bits");
                Expect(LdfTokKind.Comma);
                Db.J2602MasterResponseTolerance = ExpectNumber();
                if (LookIdent("%") || Cur.Text == "%") Take();
            }
            Expect(LdfTokKind.Semi);
            if (LookIdent("Slaves") || LookIdent("Responders"))
            {
                Take();
                Expect(LdfTokKind.Colon);
                Db.Slaves.Add(ExpectIdent());
                while (Look(LdfTokKind.Comma))
                {
                    Take();
                    Db.Slaves.Add(ExpectIdent());
                }
                Expect(LdfTokKind.Semi);
            }
            Expect(LdfTokKind.RBrace);
        }

        private void ParseComposite()
        {
            Take(); Expect(LdfTokKind.LBrace);
            while (!Look(LdfTokKind.RBrace) && !AtEof)
            {
                ExpectKw("configuration");
                var cfg = new LdfCompositeConfig { Name = ExpectIdent() };
                Expect(LdfTokKind.LBrace);
                while (!Look(LdfTokKind.RBrace) && !AtEof)
                {
                    var logical = ExpectIdent();
                    Expect(LdfTokKind.LBrace);
                    var nodes = new List<string> { ExpectIdent() };
                    while (Look(LdfTokKind.Comma)) { Take(); nodes.Add(ExpectIdent()); }
                    Expect(LdfTokKind.RBrace);
                    cfg.Items.Add((logical, nodes));
                }
                Expect(LdfTokKind.RBrace);
                Db.CompositeConfigs.Add(cfg);
            }
            Expect(LdfTokKind.RBrace);
        }

        private void ParseDiagnosticAddresses()
        {
            Take(); Expect(LdfTokKind.LBrace);
            while (!Look(LdfTokKind.RBrace) && !AtEof)
            {
                var node = ExpectIdent();
                Expect(LdfTokKind.Colon);
                var nad = ExpectInt();
                Expect(LdfTokKind.Semi);
                Db.DiagnosticAddresses[node] = nad;
                var existing = Db.NodeAttributes.FirstOrDefault(a => a.Name == node);
                if (existing != null) existing.ConfiguredNad = nad;
                else Db.NodeAttributes.Add(new LdfNodeAttr { Name = node, ConfiguredNad = nad, Protocol = Db.ProtocolVersion });
            }
            Expect(LdfTokKind.RBrace);
        }

        private void ParseSignals()
        {
            Take(); Expect(LdfTokKind.LBrace);
            while (!Look(LdfTokKind.RBrace) && !AtEof)
            {
                var name = ExpectIdent();
                Expect(LdfTokKind.Colon);
                var size = ExpectInt();
                Expect(LdfTokKind.Comma);
                var init = ParseInitValue();
                Expect(LdfTokKind.Comma);
                var pub = ExpectIdent();
                var sig = new LdfSignal { Name = name, SizeBits = size, InitValue = init, Publisher = pub };
                while (Look(LdfTokKind.Comma))
                {
                    Take();
                    sig.Subscribers.Add(ExpectIdent());
                }
                Expect(LdfTokKind.Semi);
                Db.Signals.Add(sig);
            }
            Expect(LdfTokKind.RBrace);
        }

        private string ParseInitValue()
        {
            if (Look(LdfTokKind.LBrace))
            {
                Take();
                var parts = new List<string> { ExpectInt().ToString(CultureInfo.InvariantCulture) };
                while (Look(LdfTokKind.Comma))
                {
                    Take();
                    parts.Add(ExpectInt().ToString(CultureInfo.InvariantCulture));
                }
                Expect(LdfTokKind.RBrace);
                return "{" + string.Join(", ", parts) + "}";
            }
            return ExpectInt().ToString(CultureInfo.InvariantCulture);
        }

        private void ParseDiagnosticSignals()
        {
            Take(); Expect(LdfTokKind.LBrace);
            while (!Look(LdfTokKind.RBrace) && !AtEof)
            {
                var name = ExpectIdent();
                Expect(LdfTokKind.Colon);
                var size = ExpectInt();
                Expect(LdfTokKind.Comma);
                var init = ExpectInt();
                Expect(LdfTokKind.Semi);
                Db.DiagnosticSignals.Add(new LdfSignal { Name = name, SizeBits = size, InitValue = init.ToString(CultureInfo.InvariantCulture) });
            }
            Expect(LdfTokKind.RBrace);
        }

        private void ParseFrames()
        {
            Take(); Expect(LdfTokKind.LBrace);
            while (!Look(LdfTokKind.RBrace) && !AtEof)
            {
                var frameName = ExpectIdent();
                Expect(LdfTokKind.Colon);
                var frameId = ExpectInt();
                Expect(LdfTokKind.Comma);
                var publisher = ExpectIdent();
                int length = 0;
                if (Look(LdfTokKind.Comma))
                {
                    Take();
                    length = ExpectInt();
                }
                bool isDiag = frameName is "MasterReq" or "SlaveResp" or "CommanderReq" or "ResponderResp"
                    || frameId is 0x3C or 0x3D;
                var frame = new LdfFrame
                {
                    Name = frameName,
                    Id = frameId,
                    Publisher = publisher,
                    Length = length,
                    Kind = isDiag ? "Diagnostic" : "Unconditional"
                };
                Expect(LdfTokKind.LBrace);
                int maxEnd = 0;
                while (!Look(LdfTokKind.RBrace) && !AtEof)
                {
                    var sn = ExpectIdent();
                    Expect(LdfTokKind.Comma);
                    var start = ExpectInt();
                    Expect(LdfTokKind.Semi);
                    frame.Signals.Add(new LdfFrameSignal { Name = sn, StartBit = start });
                    var bits = Db.FindSignal(sn)?.SizeBits ?? 1;
                    maxEnd = Math.Max(maxEnd, start + bits);
                }
                Expect(LdfTokKind.RBrace);
                if (frame.Length <= 0 && maxEnd > 0) frame.Length = (maxEnd + 7) / 8;
                if (frame.Length <= 0) frame.Length = 8;
                Db.Frames.Add(frame);
            }
            Expect(LdfTokKind.RBrace);
        }

        private void ParseSporadic()
        {
            Take(); Expect(LdfTokKind.LBrace);
            while (!Look(LdfTokKind.RBrace) && !AtEof)
            {
                var sf = new LdfSporadicFrame { Name = ExpectIdent() };
                Expect(LdfTokKind.Colon);
                sf.FrameNames.Add(ExpectIdent());
                while (Look(LdfTokKind.Comma)) { Take(); sf.FrameNames.Add(ExpectIdent()); }
                Expect(LdfTokKind.Semi);
                Db.SporadicFrames.Add(sf);
            }
            Expect(LdfTokKind.RBrace);
        }

        private void ParseEventTriggered()
        {
            Take(); Expect(LdfTokKind.LBrace);
            while (!Look(LdfTokKind.RBrace) && !AtEof)
            {
                var et = new LdfEventTriggeredFrame { Name = ExpectIdent() };
                Expect(LdfTokKind.Colon);
                et.CollisionResolvingSchedule = ExpectIdent();
                Expect(LdfTokKind.Comma);
                et.Id = ExpectInt();
                while (Look(LdfTokKind.Comma)) { Take(); et.FrameNames.Add(ExpectIdent()); }
                Expect(LdfTokKind.Semi);
                Db.EventTriggeredFrames.Add(et);
            }
            Expect(LdfTokKind.RBrace);
        }

        private void ParseDiagnosticFrames()
        {
            Take(); Expect(LdfTokKind.LBrace);
            var reqName = LookIdent("CommanderReq") ? "CommanderReq" : "MasterReq";
            ExpectIdent();
            Expect(LdfTokKind.Colon);
            var masterId = ExpectInt();
            var master = new LdfFrame { Name = reqName, Id = masterId, Length = 8, Kind = "Diagnostic", Publisher = Db.Master };
            Expect(LdfTokKind.LBrace);
            while (!Look(LdfTokKind.RBrace) && !AtEof)
            {
                var n = ExpectIdent(); Expect(LdfTokKind.Comma); var s = ExpectInt(); Expect(LdfTokKind.Semi);
                master.Signals.Add(new LdfFrameSignal { Name = n, StartBit = s });
            }
            Expect(LdfTokKind.RBrace);

            var respName = LookIdent("ResponderResp") ? "ResponderResp" : "SlaveResp";
            ExpectIdent();
            Expect(LdfTokKind.Colon);
            var slaveId = ExpectInt();
            var slave = new LdfFrame { Name = respName, Id = slaveId, Length = 8, Kind = "Diagnostic", Publisher = Db.Slaves.FirstOrDefault() ?? "" };
            Expect(LdfTokKind.LBrace);
            while (!Look(LdfTokKind.RBrace) && !AtEof)
            {
                var n = ExpectIdent(); Expect(LdfTokKind.Comma); var s = ExpectInt(); Expect(LdfTokKind.Semi);
                slave.Signals.Add(new LdfFrameSignal { Name = n, StartBit = s });
            }
            Expect(LdfTokKind.RBrace);
            Expect(LdfTokKind.RBrace);

            Db.Frames.RemoveAll(f => f.IsDiagnostic && (
                f.Name is "MasterReq" or "SlaveResp" or "CommanderReq" or "ResponderResp"));
            Db.Frames.Add(master);
            Db.Frames.Add(slave);
        }

        private void ParseNodeAttributes()
        {
            Take(); Expect(LdfTokKind.LBrace);
            while (!Look(LdfTokKind.RBrace) && !AtEof)
            {
                var attr = new LdfNodeAttr { Name = ExpectIdent() };
                Expect(LdfTokKind.LBrace);
                ExpectKw("LIN_protocol"); Expect(LdfTokKind.Eq);
                attr.Protocol = Look(LdfTokKind.String) ? ExpectString() : ExpectNumber().ToString(CultureInfo.InvariantCulture);
                Expect(LdfTokKind.Semi);
                ExpectKw("configured_NAD"); Expect(LdfTokKind.Eq); attr.ConfiguredNad = ExpectInt(); Expect(LdfTokKind.Semi);
                if (LookIdent("initial_NAD"))
                {
                    Take(); Expect(LdfTokKind.Eq); attr.InitialNad = ExpectInt(); attr.HasInitialNad = true; Expect(LdfTokKind.Semi);
                }
                while (!Look(LdfTokKind.RBrace) && !AtEof)
                    ParseNodeBody(attr);
                Expect(LdfTokKind.RBrace);
                Db.NodeAttributes.Add(attr);
            }
            Expect(LdfTokKind.RBrace);
        }

        private void ParseNodeBody(LdfNodeAttr na)
        {
            if (LookIdent("product_id"))
            {
                Take(); Expect(LdfTokKind.Eq);
                na.SupplierId = Take().Text; Expect(LdfTokKind.Comma);
                na.FunctionId = Take().Text;
                if (Look(LdfTokKind.Comma)) { Take(); na.VariantId = Take().Text; }
                Expect(LdfTokKind.Semi); return;
            }
            if (LookIdent("response_error")) { Take(); Expect(LdfTokKind.Eq); na.ResponseError = ExpectIdent(); Expect(LdfTokKind.Semi); return; }
            if (LookIdent("fault_state_signals"))
            {
                Take(); Expect(LdfTokKind.Eq);
                na.FaultStateSignals.Add(ExpectIdent());
                while (Look(LdfTokKind.Comma)) { Take(); na.FaultStateSignals.Add(ExpectIdent()); }
                Expect(LdfTokKind.Semi); return;
            }
            if (LookIdent("P2_min")) { Take(); Expect(LdfTokKind.Eq); na.P2MinMs = ExpectNumber(); ExpectKw("ms"); Expect(LdfTokKind.Semi); return; }
            if (LookIdent("ST_min")) { Take(); Expect(LdfTokKind.Eq); na.StMinMs = ExpectNumber(); ExpectKw("ms"); Expect(LdfTokKind.Semi); return; }
            if (LookIdent("N_As_timeout")) { Take(); Expect(LdfTokKind.Eq); na.NAsTimeoutMs = ExpectNumber(); ExpectKw("ms"); Expect(LdfTokKind.Semi); return; }
            if (LookIdent("N_Cr_timeout")) { Take(); Expect(LdfTokKind.Eq); na.NCrTimeoutMs = ExpectNumber(); ExpectKw("ms"); Expect(LdfTokKind.Semi); return; }
            if (LookIdent("response_tolerance") || LookIdent("wakeup_time") || LookIdent("poweron_time"))
            {
                Take(); Expect(LdfTokKind.Eq); ExpectNumber();
                if (LookIdent("ms") || Cur.Text == "%") Take();
                Expect(LdfTokKind.Semi); return;
            }
            if (LookIdent("configurable_frames"))
            {
                Take(); Expect(LdfTokKind.LBrace);
                while (!Look(LdfTokKind.RBrace) && !AtEof)
                {
                    na.ConfigurableFrames.Add(ExpectIdent());
                    if (Look(LdfTokKind.Eq)) { Take(); ExpectInt(); }
                    Expect(LdfTokKind.Semi);
                }
                Expect(LdfTokKind.RBrace); return;
            }
            throw Fail($"unknown node attribute field '{Cur.Text}'");
        }

        private void ParseSchedules()
        {
            Take(); Expect(LdfTokKind.LBrace);
            while (!Look(LdfTokKind.RBrace) && !AtEof)
            {
                var table = new LdfScheduleTable { Name = ExpectIdent() };
                Expect(LdfTokKind.LBrace);
                while (!Look(LdfTokKind.RBrace) && !AtEof)
                {
                    var slot = ParseScheduleCmd();
                    ExpectKw("delay");
                    slot.DelayMs = ExpectNumber();
                    ExpectKw("ms");
                    Expect(LdfTokKind.Semi);
                    table.Slots.Add(slot);
                }
                Expect(LdfTokKind.RBrace);
                Db.Schedules.Add(table);
            }
            Expect(LdfTokKind.RBrace);
        }

        private LdfScheduleSlot ParseScheduleCmd()
        {
            var e = new LdfScheduleSlot();
            var name = ExpectIdent();
            e.Command = name;
            if (Look(LdfTokKind.LBrace))
            {
                Take();
                switch (name)
                {
                    case "AssignNAD":
                    case "SaveConfiguration":
                        e.NodeName = ExpectIdent();
                        break;
                    case "ConditionalChangeNAD":
                        for (int i = 0; i < 6; i++)
                        {
                            if (i > 0) Expect(LdfTokKind.Comma);
                            e.IntArgs.Add(ExpectInt());
                        }
                        break;
                    case "DataDump":
                        e.NodeName = ExpectIdent();
                        for (int i = 0; i < 5; i++) { Expect(LdfTokKind.Comma); e.IntArgs.Add(ExpectInt()); }
                        break;
                    case "AssignFrameIdRange":
                        e.NodeName = ExpectIdent();
                        Expect(LdfTokKind.Comma);
                        e.StartIndex = ExpectInt();
                        while (Look(LdfTokKind.Comma))
                        {
                            Take();
                            e.Pids.Add(ExpectInt());
                            e.HasExplicitPids = true;
                        }
                        break;
                    case "FreeFormat":
                        for (int i = 0; i < 8; i++)
                        {
                            if (i > 0) Expect(LdfTokKind.Comma);
                            e.IntArgs.Add(ExpectInt());
                        }
                        break;
                    case "AssignFrameId":
                    case "UnassignFrameId":
                        e.NodeName = ExpectIdent();
                        Expect(LdfTokKind.Comma);
                        e.AssignedFrame = ExpectIdent();
                        break;
                    default:
                        /* skip until RBrace */
                        while (!Look(LdfTokKind.RBrace) && !AtEof) Take();
                        break;
                }
                Expect(LdfTokKind.RBrace);
                e.Command = name + "{...}";
            }
            LdfParseSupport.FillScheduleSlot(e, name);
            return e;
        }

        private void ParseSignalGroups()
        {
            Take(); Expect(LdfTokKind.LBrace);
            while (!Look(LdfTokKind.RBrace) && !AtEof)
            {
                var g = new LdfSignalGroup { Name = ExpectIdent() };
                Expect(LdfTokKind.Colon);
                g.SizeBits = ExpectInt();
                Expect(LdfTokKind.LBrace);
                while (!Look(LdfTokKind.RBrace) && !AtEof)
                {
                    var sn = ExpectIdent(); Expect(LdfTokKind.Comma); var off = ExpectInt(); Expect(LdfTokKind.Semi);
                    g.Members.Add((sn, off));
                }
                Expect(LdfTokKind.RBrace);
                Db.SignalGroups.Add(g);
            }
            Expect(LdfTokKind.RBrace);
        }

        private void ParseEncodings()
        {
            Take(); Expect(LdfTokKind.LBrace);
            while (!Look(LdfTokKind.RBrace) && !AtEof)
            {
                var enc = new LdfEncoding { Name = ExpectIdent() };
                Expect(LdfTokKind.LBrace);
                while (!Look(LdfTokKind.RBrace) && !AtEof)
                {
                    var ev = new LdfEncodingValue();
                    var kind = ExpectIdent();
                    if (kind == "logical_value")
                    {
                        ev.Kind = "logical";
                        Expect(LdfTokKind.Comma); ev.Value = ExpectLong();
                        if (Look(LdfTokKind.Comma)) { Take(); ev.Text = ExpectString(); }
                    }
                    else if (kind == "physical_value")
                    {
                        ev.Kind = "physical";
                        Expect(LdfTokKind.Comma); ev.MinValue = ExpectLong();
                        Expect(LdfTokKind.Comma); ev.MaxValue = ExpectLong();
                        Expect(LdfTokKind.Comma); ev.Scale = ExpectNumber();
                        Expect(LdfTokKind.Comma); ev.Offset = ExpectNumber();
                        if (Look(LdfTokKind.Comma)) { Take(); ev.Text = ExpectString(); }
                    }
                    else if (kind == "bcd_value") ev.Kind = "bcd";
                    else if (kind == "ascii_value") ev.Kind = "ascii";
                    else if (kind == "float_value") ev.Kind = "float";
                    Expect(LdfTokKind.Semi);
                    enc.Values.Add(ev);
                }
                Expect(LdfTokKind.RBrace);
                Db.Encodings.Add(enc);
            }
            Expect(LdfTokKind.RBrace);
        }

        private void ParseRepresentations()
        {
            Take(); Expect(LdfTokKind.LBrace);
            while (!Look(LdfTokKind.RBrace) && !AtEof)
            {
                var rep = new LdfRepresentation { Encoding = ExpectIdent() };
                Expect(LdfTokKind.Colon);
                rep.Signals.Add(ExpectIdent());
                while (Look(LdfTokKind.Comma)) { Take(); rep.Signals.Add(ExpectIdent()); }
                Expect(LdfTokKind.Semi);
                Db.Representations.Add(rep);
            }
            Expect(LdfTokKind.RBrace);
        }
    }
}
