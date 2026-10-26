using System.Globalization;
using System.IO;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using LdfEditor.Antlr;
using LdfEditor.Models;

namespace LdfEditor.Services;

public sealed class NcfAntlrLoader
{
    public NcfFile Load(string path)
    {
        var text = File.ReadAllText(path);
        return LoadText(text);
    }

    public NcfFile LoadText(string text)
    {
        var input = new AntlrInputStream(text);
        var lexer = new NcfLexer(input);
        var tokens = new CommonTokenStream(lexer);
        var parser = new NcfParser(tokens);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new ThrowingErrorListener());
        var tree = parser.ncfFile();
        var v = new NcfModelVisitor();
        v.Visit(tree);
        return v.File;
    }

    private sealed class ThrowingErrorListener : BaseErrorListener
    {
        public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e)
        {
            throw new InvalidDataException($"NCF parse error L{line}:{charPositionInLine} {msg}");
        }
    }

    private sealed class NcfModelVisitor : NcfBaseVisitor<object?>
    {
        public NcfFile File { get; } = new();
        private NcfNode? _node;
        private NcfFrame? _frame;
        private NcfSignal? _signal;
        private NcfEncoding? _encoding;

        public override object? VisitLanguageVersion(NcfParser.LanguageVersionContext context)
        {
            File.LanguageVersion = Unquote(context.STRING().GetText());
            return null;
        }

        public override object? VisitFileRevision(NcfParser.FileRevisionContext context)
        {
            File.FileRevision = Unquote(context.STRING().GetText());
            return null;
        }

        public override object? VisitSigByteOrder(NcfParser.SigByteOrderContext context)
        {
            File.IsMotorolaByteOrder = context.GetText().Contains("big_endian", StringComparison.Ordinal);
            return null;
        }

        public override object? VisitNodeDef(NcfParser.NodeDefContext context)
        {
            _node = new NcfNode { Name = context.identifier().GetText() };
            VisitChildren(context);
            File.Nodes.Add(_node);
            _node = null;
            return null;
        }

        public override object? VisitGeneralItem(NcfParser.GeneralItemContext context)
        {
            if (_node == null) return null;
            var t = context.GetText();
            if (t.StartsWith("LIN_protocol_version", StringComparison.Ordinal))
                _node.ProtocolVersion = Unquote(context.STRING().GetText());
            else if (t.StartsWith("supplier", StringComparison.Ordinal))
                _node.Supplier = ParseLong(context.number().GetText());
            else if (t.StartsWith("function", StringComparison.Ordinal))
                _node.Function = ParseLong(context.number().GetText());
            else if (t.StartsWith("variant", StringComparison.Ordinal))
                _node.Variant = ParseLong(context.number().GetText());
            else if (t.StartsWith("bitrate", StringComparison.Ordinal))
            {
                var bd = context.bitrateDef();
                if (bd.number().Length > 0 && bd.GetText().Contains("kbps")
                    && !bd.GetText().StartsWith("automatic", StringComparison.Ordinal)
                    && !bd.GetText().StartsWith("select", StringComparison.Ordinal))
                    _node.BitrateKbps = ParseDouble(bd.number(0).GetText());
                else if (bd.GetText().StartsWith("automatic", StringComparison.Ordinal) && bd.number().Length > 0)
                    _node.BitrateKbps = ParseDouble(bd.number(0).GetText());
            }
            else if (t.StartsWith("sends_wake_up_signal", StringComparison.Ordinal))
                _node.SendsWakeUp = Unquote(context.STRING().GetText());
            else if (t.StartsWith("response_tolerance", StringComparison.Ordinal))
                _node.ResponseTolerancePct = ParseDouble(context.number().GetText());
            else if (t.StartsWith("wakeup_time", StringComparison.Ordinal))
                _node.WakeupTimeMs = (int)ParseLong(context.number().GetText());
            else if (t.StartsWith("poweron_time", StringComparison.Ordinal))
                _node.PoweronTimeMs = (int)ParseLong(context.number().GetText());
            return null;
        }

        public override object? VisitDiagnosticItem(NcfParser.DiagnosticItemContext context)
        {
            if (_node == null) return null;
            var t = context.GetText();
            if (t.StartsWith("NAD", StringComparison.Ordinal))
            {
                var nv = context.nadValue();
                var nums = nv.number();
                if (nv.GetText().Contains("to", StringComparison.Ordinal) && nums.Length >= 2)
                {
                    _node.Nad = (int)ParseLong(nums[0].GetText());
                    _node.NadTo = (int)ParseLong(nums[1].GetText());
                }
                else if (nums.Length > 1)
                {
                    foreach (var n in nums)
                        _node.NadList.Add((int)ParseLong(n.GetText()));
                    _node.Nad = _node.NadList[0];
                }
                else if (nums.Length == 1)
                    _node.Nad = (int)ParseLong(nums[0].GetText());
            }
            else if (t.StartsWith("diagnostic_class", StringComparison.Ordinal))
                _node.DiagnosticClass = (int)ParseLong(context.INT().GetText());
            else if (t.StartsWith("P2_min", StringComparison.Ordinal))
                _node.P2MinMs = ParseDouble(context.number(0).GetText());
            else if (t.StartsWith("ST_min", StringComparison.Ordinal))
                _node.StMinMs = ParseDouble(context.number(0).GetText());
            else if (t.StartsWith("N_As_timeout", StringComparison.Ordinal))
                _node.NAsTimeoutMs = ParseDouble(context.number(0).GetText());
            else if (t.StartsWith("N_Cr_timeout", StringComparison.Ordinal))
                _node.NCrTimeoutMs = ParseDouble(context.number(0).GetText());
            else if (t.StartsWith("support_sid", StringComparison.Ordinal))
            {
                foreach (var n in context.number())
                    _node.SupportSids.Add((int)ParseLong(n.GetText()));
            }
            return null;
        }

        public override object? VisitFrameRole(NcfParser.FrameRoleContext context)
        {
            if (_node == null) return null;
            bool publish = context.GetText().StartsWith("publish", StringComparison.Ordinal);
            _frame = new NcfFrame
            {
                Name = context.identifier().GetText(),
                IsPublish = publish
            };
            VisitChildren(context);
            _node.Frames.Add(_frame);
            _frame = null;
            return null;
        }

        public override object? VisitFrameProp(NcfParser.FramePropContext context)
        {
            if (_frame == null) return null;
            var t = context.GetText();
            if (t.StartsWith("message_ID", StringComparison.Ordinal) || t.StartsWith("message_id", StringComparison.OrdinalIgnoreCase))
                _frame.MessageId = (int)ParseLong(context.number().GetText());
            else if (t.StartsWith("frame_id", StringComparison.OrdinalIgnoreCase))
                _frame.FrameId = (int)ParseLong(context.number().GetText());
            else if (t.StartsWith("length", StringComparison.Ordinal) || t.StartsWith("frame_length", StringComparison.Ordinal))
                _frame.Length = (int)ParseLong(context.INT().GetText());
            else if (t.StartsWith("event_triggered_frame", StringComparison.Ordinal))
                _frame.EventTriggeredFrame = context.identifier().GetText();
            else if (t.StartsWith("signals", StringComparison.Ordinal))
                VisitChildren(context);
            return null;
        }

        public override object? VisitSignalDef(NcfParser.SignalDefContext context)
        {
            if (_frame == null) return null;
            _signal = new NcfSignal { Name = context.identifier().GetText() };
            VisitChildren(context);
            _frame.Signals.Add(_signal);
            _signal = null;
            return null;
        }

        public override object? VisitSignalProp(NcfParser.SignalPropContext context)
        {
            if (_signal == null) return null;
            var t = context.GetText();
            if (t.StartsWith("init_value", StringComparison.Ordinal))
                _signal.InitValue = context.initValue().GetText().Replace(" ", "");
            else if (t.StartsWith("size", StringComparison.Ordinal))
                _signal.SizeBits = (int)ParseLong(context.INT().GetText());
            else if (t.StartsWith("offset", StringComparison.Ordinal))
                _signal.Offset = (int)ParseLong(context.INT().GetText());
            else if (t.StartsWith("encoding", StringComparison.Ordinal))
            {
                var enc = new NcfEncoding { Name = context.identifier().GetText() };
                _encoding = enc;
                foreach (var ev in context.encodingValue())
                    Visit(ev);
                _signal.InlineEncoding = enc;
                _encoding = null;
            }
            else if (context.identifier() != null && !t.Contains('='))
                _signal.EncodingRef = context.identifier().GetText();
            return null;
        }

        public override object? VisitEncodingType(NcfParser.EncodingTypeContext context)
        {
            if (_node == null) return null;
            _encoding = new NcfEncoding { Name = context.identifier().GetText() };
            VisitChildren(context);
            _node.Encodings.Add(_encoding);
            _encoding = null;
            return null;
        }

        public override object? VisitEncodingValue(NcfParser.EncodingValueContext context)
        {
            if (_encoding == null) return null;
            var t = context.GetText();
            if (t.StartsWith("logical_value", StringComparison.Ordinal))
            {
                _encoding.Values.Add(new LdfEncodingValue
                {
                    Kind = "logical",
                    Value = ParseLong(context.INT(0).GetText()),
                    Text = context.STRING() != null ? Unquote(context.STRING().GetText()) : ""
                });
            }
            else if (t.StartsWith("physical_value", StringComparison.Ordinal))
            {
                var ints = context.INT();
                var nums = context.number();
                _encoding.Values.Add(new LdfEncodingValue
                {
                    Kind = "physical",
                    MinValue = ParseLong(ints[0].GetText()),
                    MaxValue = ParseLong(ints[1].GetText()),
                    Scale = nums.Length > 0 ? ParseDouble(nums[0].GetText()) : 1,
                    Offset = nums.Length > 1 ? ParseDouble(nums[1].GetText()) : 0,
                    Text = context.STRING() != null ? Unquote(context.STRING().GetText()) : ""
                });
            }
            else if (t.StartsWith("ascii_value", StringComparison.Ordinal))
                _encoding.Values.Add(new LdfEncodingValue { Kind = "ascii" });
            else if (t.StartsWith("bcd_value", StringComparison.Ordinal))
                _encoding.Values.Add(new LdfEncodingValue { Kind = "bcd" });
            else if (t.StartsWith("float_value", StringComparison.Ordinal))
                _encoding.Values.Add(new LdfEncodingValue { Kind = "float" });
            return null;
        }

        public override object? VisitStatusItem(NcfParser.StatusItemContext context)
        {
            if (_node == null) return null;
            var t = context.GetText();
            var ids = context.identifier();
            if (t.StartsWith("response_error", StringComparison.Ordinal) && ids.Length > 0)
                _node.ResponseError = ids[0].GetText();
            else if (t.StartsWith("reset_indication", StringComparison.Ordinal) && ids.Length > 0)
                _node.ResetIndication = ids[0].GetText();
            else if (t.StartsWith("fault_state_signals", StringComparison.Ordinal))
            {
                foreach (var id in ids)
                    _node.FaultStateSignals.Add(id.GetText());
            }
            return null;
        }

        private static string Unquote(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
                return s[1..^1].Replace("\\\"", "\"");
            return s;
        }

        private static long ParseLong(string s)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return long.Parse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return long.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static double ParseDouble(string s) =>
            double.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
