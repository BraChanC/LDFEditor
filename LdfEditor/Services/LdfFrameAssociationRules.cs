using LdfEditor.Models;

namespace LdfEditor.Services;

/// <summary>
/// Vector association rules for Event-Triggered / Sporadic frames
/// (CannotAssignAssociatedFrameWithMasterAsPublisher, ValidateETFTransmitter, LIN 2.2A).
/// </summary>
public static class LdfFrameAssociationRules
{
    public static bool IsAssociableUnconditional(LdfFrame? frame) =>
        frame != null && !frame.IsDiagnostic;

    /// <summary>UI-time check before adding a frame to an ETF.</summary>
    public static bool CanAssociateToEventTriggered(LdfDatabase db, LdfEventTriggeredFrame et,
        LdfFrame frame, out string error)
    {
        error = "";
        if (!IsAssociableUnconditional(frame))
        {
            error = "Only unconditional frames can be associated.";
            return false;
        }

        if (et.FrameNames.Any(n => string.Equals(n, frame.Name, StringComparison.OrdinalIgnoreCase)))
        {
            error = $"Frame <{frame.Name}> is already associated with Event-Triggered Frame <{et.Name}>.";
            return false;
        }

        if (IsMasterPublisher(db, frame))
        {
            error =
                $"Cannot assign unconditional frame <{frame.Name}> because its publisher is the master node. " +
                "The master node cannot be publisher of an Event-triggered frame!";
            return false;
        }

        var existing = ResolveAssociated(db, et.FrameNames).ToList();
        if (existing.Count > 0)
        {
            int expectLen = existing[0].Length;
            if (frame.Length != expectLen)
            {
                error =
                    $"Cannot assign unconditional frame <{frame.Name}>: associated frames of " +
                    $"Event-Triggered Frame <{et.Name}> must have the same length ({expectLen} bytes).";
                return false;
            }

            if (existing.Any(f =>
                    string.Equals(f.Publisher, frame.Publisher, StringComparison.OrdinalIgnoreCase)))
            {
                error =
                    $"Cannot assign unconditional frame <{frame.Name}>: publisher <{frame.Publisher}> " +
                    $"is already used by another associated frame of <{et.Name}>. " +
                    "Associated frames of an Event-Triggered Frame must be published by different slave nodes.";
                return false;
            }
        }

        return true;
    }

    /// <summary>UI-time check before adding a frame to a Sporadic frame.</summary>
    public static bool CanAssociateToSporadic(LdfDatabase db, LdfSporadicFrame sp,
        LdfFrame frame, out string error)
    {
        error = "";
        if (!IsAssociableUnconditional(frame))
        {
            error = "Only unconditional frames can be associated.";
            return false;
        }

        if (sp.FrameNames.Any(n => string.Equals(n, frame.Name, StringComparison.OrdinalIgnoreCase)))
        {
            error = $"Frame <{frame.Name}> is already associated with Sporadic Frame <{sp.Name}>.";
            return false;
        }

        /* Sporadic frames are master-side; associated frames should be published by the master. */
        if (!IsMasterPublisher(db, frame))
        {
            error =
                $"Cannot assign unconditional frame <{frame.Name}> because its publisher is not the master node. " +
                "Associated frames of a Sporadic Frame must be published by the master.";
            return false;
        }

        return true;
    }

    public static bool TryValidateEventTriggered(LdfDatabase db, LdfEventTriggeredFrame et,
        Action<string, string> addError)
    {
        bool ok = true;
        if (string.IsNullOrWhiteSpace(et.CollisionResolvingSchedule))
        {
            addError(et.Name,
                $"ETF '{et.Name}': collision resolving schedule is missing");
            ok = false;
        }
        else if (db.Schedules.All(s =>
                     !string.Equals(s.Name, et.CollisionResolvingSchedule, StringComparison.OrdinalIgnoreCase)))
        {
            addError(et.Name,
                $"ETF '{et.Name}': collision schedule '{et.CollisionResolvingSchedule}' not found");
            ok = false;
        }

        if (et.Id is < 0 or > 59)
        {
            addError(et.Name, $"ETF '{et.Name}': frame ID {et.Id} must be 0..59");
            ok = false;
        }

        var resolved = new List<LdfFrame>();
        foreach (var fn in et.FrameNames)
        {
            var fr = db.Frames.FirstOrDefault(f =>
                string.Equals(f.Name, fn, StringComparison.OrdinalIgnoreCase));
            if (fr == null)
            {
                addError(et.Name, $"ETF '{et.Name}': associated frame '{fn}' not found");
                ok = false;
                continue;
            }
            if (fr.IsDiagnostic)
            {
                addError(et.Name,
                    $"ETF '{et.Name}': associated frame '{fn}' must be unconditional");
                ok = false;
                continue;
            }
            if (IsMasterPublisher(db, fr))
            {
                addError(et.Name,
                    $"The publisher of Event-triggered frame <{et.Name}> is the master node (check associated frame <{fn}>).");
                ok = false;
            }
            resolved.Add(fr);
        }

        if (resolved.Count >= 2)
        {
            int len0 = resolved[0].Length;
            foreach (var fr in resolved.Skip(1))
            {
                if (fr.Length != len0)
                {
                    addError(et.Name,
                        $"ETF '{et.Name}': associated frames must have equal length " +
                        $"(<{resolved[0].Name}>={len0}, <{fr.Name}>={fr.Length})");
                    ok = false;
                    break;
                }
            }

            var pubs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fr in resolved)
            {
                if (string.IsNullOrEmpty(fr.Publisher)) continue;
                if (!pubs.Add(fr.Publisher))
                {
                    addError(et.Name,
                        $"ETF '{et.Name}': associated frames must have distinct publishers " +
                        $"(duplicate publisher <{fr.Publisher}>)");
                    ok = false;
                    break;
                }
            }
        }

        return ok;
    }

    public static bool TryValidateSporadic(LdfDatabase db, LdfSporadicFrame sp,
        Action<string, string> addError)
    {
        bool ok = true;
        foreach (var fn in sp.FrameNames)
        {
            var fr = db.Frames.FirstOrDefault(f =>
                string.Equals(f.Name, fn, StringComparison.OrdinalIgnoreCase));
            if (fr == null)
            {
                addError(sp.Name, $"Sporadic '{sp.Name}': associated frame '{fn}' not found");
                ok = false;
                continue;
            }
            if (fr.IsDiagnostic)
            {
                addError(sp.Name,
                    $"Sporadic '{sp.Name}': associated frame '{fn}' must be unconditional");
                ok = false;
                continue;
            }
            if (!IsMasterPublisher(db, fr))
            {
                addError(sp.Name,
                    $"Sporadic '{sp.Name}': associated frame '{fn}' must be published by the master " +
                    $"(publisher is <{fr.Publisher}>)");
                ok = false;
            }
        }
        return ok;
    }

    public static bool IsMasterPublisher(LdfDatabase db, LdfFrame frame) =>
        !string.IsNullOrEmpty(db.Master)
        && string.Equals(frame.Publisher, db.Master, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<LdfFrame> ResolveAssociated(LdfDatabase db, IEnumerable<string> names)
    {
        foreach (var n in names)
        {
            var fr = db.Frames.FirstOrDefault(f =>
                string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase) && !f.IsDiagnostic);
            if (fr != null) yield return fr;
        }
    }
}
