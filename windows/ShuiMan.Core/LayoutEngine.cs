namespace ShuiMan.Core;

/// <summary>Groups source positions without changing their identity or consuming one twice.</summary>
public static class LayoutEngine
{
    public static int Rotation(ReadingUnit unit, SavedBook state,
        IReadOnlyDictionary<string, SpreadDecision>? decisions = null)
    {
        if (state.Overrides.TryGetValue(unit.Id, out var correction) && correction.Rotation is int manual)
            return NormalizeRotation(manual);
        if (unit.RotationHint is int publisher) return NormalizeRotation(publisher);
        if (!state.Preferences.AutomaticOrientation || unit.IsCover) return 0;
        return NormalizeRotation(decisions?.GetValueOrDefault(unit.Id)?.Rotation ?? 0);
    }

    public static List<DisplayGroup> Groups(Publication publication, SavedBook state,
        IReadOnlyDictionary<string, SpreadDecision>? decisions = null,
        IReadOnlyDictionary<int, PairDecision>? pairs = null, bool wideScreen = true)
    {
        var units = publication.Units;
        var preferences = state.Preferences;
        var doublePage = preferences.Layout == "double" || preferences.Layout == "auto" && wideScreen;
        PageOverride? Correction(int i) => state.Overrides.GetValueOrDefault(units[i].Id);
        bool Alone(int index)
        {
            var unit = units[index];
            if (unit.Complex || unit.Error != null) return true;
            if (Correction(index)?.Standalone is bool standalone) return standalone;
            if (preferences.CoverAlone && unit.IsCover) return true;
            var angle = Rotation(unit, state, decisions);
            var width = angle % 180 == 90 ? unit.Height : unit.Width;
            var height = angle % 180 == 90 ? unit.Width : unit.Height;
            return preferences.SmartSpreads &&
                   (decisions?.GetValueOrDefault(unit.Id) is { Standalone: true } decision &&
                        (decision.Rotation == 0 || preferences.AutomaticOrientation) || height > 0 && width / height >= 1.2)
                   || angle != 0;
        }

        var automatic = new Dictionary<int, PairDecision>();
        if (preferences.SmartSpreads && preferences.AutomaticPairs && pairs != null)
            foreach (var (index, pair) in pairs)
            {
                if (index < 0 || index >= units.Count - 1 || pair.FirstIndex != index ||
                    !(pair.Automatic || preferences.AggressivePairs && pair.Suggested)) continue;
                var a = units[index];
                var b = units[index + 1];
                var ca = Correction(index);
                var cb = Correction(index + 1);
                if (Alone(index) || Alone(index + 1) || a.IsCover || b.IsCover || ca?.JoinNext != null ||
                    cb?.JoinNext == true || cb?.PairingBreak == true || ca?.Rotation != null || cb?.Rotation != null ||
                    !OrientationAllowsPair(a) || !OrientationAllowsPair(b) ||
                    Rotation(a, state, decisions) != 0 || Rotation(b, state, decisions) != 0) continue;
                var rival = Math.Max(pairs.GetValueOrDefault(index - 1)?.Score ?? 0,
                    pairs.GetValueOrDefault(index + 1)?.Score ?? 0);
                if (pair.Score - rival >= (preferences.AggressivePairs ? .04 : .08)) automatic[index] = pair;
            }

        bool OrientationAllowsPair(ReadingUnit unit)
        {
            // An explicit upright publisher hint already establishes orientation;
            // it need not wait for an OCR entry in the background analysis cache.
            if (unit.RotationHint is int hint) return NormalizeRotation(hint) == 0;
            return decisions?.GetValueOrDefault(unit.Id) is { Rotation: 0, Uncertain: false };
        }

        var groups = new List<DisplayGroup>();
        var i = 0;
        while (i < units.Count)
        {
            var correction = Correction(i);
            if (correction?.JoinNext == true && i + 1 < units.Count &&
                !units[i].Complex && !units[i + 1].Complex && units[i].Error == null && units[i + 1].Error == null)
            {
                // Confirmation stores physical placement. Navigation direction may subsequently change.
                groups.Add(new(correction.EarlierOnRight ? [i + 1, i] : [i, i + 1], true,
                    ValidOffset(correction.PairOffset), ValidScale(correction.PairScale)));
                i += 2;
            }
            else if (automatic.TryGetValue(i, out var pair))
            {
                groups.Add(new(pair.Swapped ? [i + 1, i] : [i, i + 1], true,
                    ValidOffset(pair.VerticalOffset), ValidScale(pair.RightScale)));
                i += 2;
            }
            else if (Alone(i) || !doublePage || i + 1 == units.Count || Alone(i + 1) ||
                     Correction(i + 1)?.JoinNext == true || Correction(i + 1)?.PairingBreak == true ||
                     automatic.ContainsKey(i + 1))
            {
                groups.Add(new([i], Alone(i)));
                i++;
            }
            else
            {
                groups.Add(new(preferences.Direction == "rtl" ? [i + 1, i] : [i, i + 1]));
                i += 2;
            }
        }
        return groups;
    }

    internal static int NormalizeRotation(int rotation) => (rotation % 360 + 360) % 360;
    private static double ValidOffset(double offset) => double.IsFinite(offset) ? Math.Clamp(offset, -1, 1) : 0;
    private static double ValidScale(double scale) => double.IsFinite(scale) && scale > 0 ? Math.Clamp(scale, .25, 4) : 1;
}
