using LdfEditor.Models;

namespace LdfEditor.Services;

/// <summary>
/// Ports Vector DefaultModelCreator.MakeConsistentDefaultModel + LinProtocol/LinLanguage strings.
/// Menu labels match Vector New File dropdown; OEM variant = Toyota LIN (vToyotaLin).
/// </summary>
public static class LdfDefaultModelCreator
{
    public static IReadOnlyList<(string MenuLabel, string DialectKey)> Dialects { get; } =
    [
        ("LDF ISO17987:2015", "iso17987"),
        ("LDF 2.2", "2.2"),
        ("LDF 2.1", "2.1"),
        ("LDF 2.0", "2.0"),
        ("LDF 1.3", "1.3"),
        ("LDF J2602:2012", "j2602-2012"),
        ("LDF J2602:2021", "j2602-2021"),
        ("OEM variant", "oem")
    ];

    public static LdfDatabase Create(string? dialectKey)
    {
        var spec = Resolve(dialectKey ?? "2.2");
        var db = new LdfDatabase
        {
            Name = "LIN_Network",
            ProtocolVersion = spec.Protocol,
            LanguageVersion = spec.Language,
            SpeedKbps = spec.Baudrate,
            ChannelName = "",
            ChannelPostfix = "",
            Comment = "",
            IsMotorolaByteOrder = spec.Motorola,
            Master = "LIN_Commander",
            TimebaseMs = 1.0,
            JitterMs = 0.0
        };
        db.Slaves.Add("LIN_Responder");

        var reqPrefix = spec.J2602_2021 ? "CommanderReqB" : "MasterReqB";
        var respPrefix = spec.J2602_2021 ? "ResponderRespB" : "SlaveRespB";
        var reqFrame = spec.J2602_2021 ? "CommanderReq" : "MasterReq";
        var respFrame = spec.J2602_2021 ? "ResponderResp" : "SlaveResp";

        for (int i = 0; i < 8; i++)
        {
            db.DiagnosticSignals.Add(new LdfSignal
            {
                Name = reqPrefix + i,
                SizeBits = 8,
                InitValue = "0",
                Publisher = db.Master
            });
        }
        for (int i = 0; i < 8; i++)
        {
            db.DiagnosticSignals.Add(new LdfSignal
            {
                Name = respPrefix + i,
                SizeBits = 8,
                InitValue = "0",
                Publisher = "LIN_Responder"
            });
        }

        var masterReq = new LdfFrame
        {
            Name = reqFrame,
            Id = 0x3C,
            Publisher = db.Master,
            Length = 8,
            Kind = "Diagnostic"
        };
        var slaveResp = new LdfFrame
        {
            Name = respFrame,
            Id = 0x3D,
            Publisher = "LIN_Responder",
            Length = 8,
            Kind = "Diagnostic"
        };
        for (int i = 0; i < 8; i++)
        {
            int offset = spec.Motorola ? i * 8 + 7 : i * 8;
            masterReq.Signals.Add(new LdfFrameSignal { Name = reqPrefix + i, StartBit = offset });
            slaveResp.Signals.Add(new LdfFrameSignal { Name = respPrefix + i, StartBit = offset });
        }
        db.Frames.Add(masterReq);
        db.Frames.Add(slaveResp);

        /* LIN 1.3 / OEM (Toyota) → Diagnostic_addresses; LIN ≥ 2.0 → Node_attributes */
        if (spec.UseDiagnosticAddresses)
        {
            db.DiagnosticAddresses["LIN_Responder"] = 255;
        }
        else
        {
            db.NodeAttributes.Add(new LdfNodeAttr
            {
                Name = "LIN_Responder",
                Protocol = spec.Protocol,
                ConfiguredNad = 255,
                InitialNad = 255,
                HasInitialNad = true,
                SupplierId = "0",
                FunctionId = "0",
                VariantId = "0",
                P2MinMs = spec.P2MinMs,
                StMinMs = 0,
                NAsTimeoutMs = 1000,
                NCrTimeoutMs = 1000
            });
        }

        return db;
    }

    private sealed record Spec(
        string Protocol,
        string Language,
        double Baudrate,
        bool Motorola,
        bool J2602_2021,
        bool UseDiagnosticAddresses,
        double P2MinMs);

    private static Spec Resolve(string key) =>
        key.Trim().ToLowerInvariant() switch
        {
            /* LinProtocol.ToString / LinLanguage.ToString */
            "iso17987" => new Spec("ISO17987:2015", "ISO17987:2015", 19.2, false, false, false, 50),
            "2.2" => new Spec("2.2", "2.2", 19.2, false, false, false, 50),
            "2.1" => new Spec("2.1", "2.1", 19.2, false, false, false, 50),
            "2.0" => new Spec("2.0", "2.0", 19.2, false, false, false, 0),
            "1.3" => new Spec("1.3", "1.3", 19.2, false, false, true, 0),
            "j2602-2012" => new Spec("J2602_1_1.0", "J2602_3_1.0", 10.417, false, false, false, 0),
            "j2602-2021" => new Spec("J2602_1_2.0", "J2602_3_2.0", 10.417, false, true, false, 0),
            /* toyotaLINToolStripMenuItem → vToyotaLin; language forced to 1.3; Motorola */
            "oem" => new Spec("OEM variant", "1.3", 19.2, true, false, true, 0),
            _ => new Spec("2.2", "2.2", 19.2, false, false, false, 50)
        };
}
