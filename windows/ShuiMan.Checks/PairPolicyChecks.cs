using ShuiMan.Core;

namespace ShuiMan.Checks;

internal static partial class Program
{
    private static void PairPolicyChecks()
    {
        Check("mac-compatible seam suggestions respect strict-mode opt-out", () =>
        {
            var publication = Pages(5);
            var state = Settings("single", "ltr");
            var directions = publication.Units.ToDictionary(unit => unit.Id, _ => new SpreadDecision());
            var pairs = new Dictionary<int, PairDecision> { [1] = new(1, false, true, .64, .02, 1.03, true, .78, .33, .09, 5, .44) };
            var groups = LayoutEngine.Groups(publication, state, directions, pairs);
            var spread = groups.Single(group => group.Indices.Contains(1));
            True(spread.Spread && spread.Indices.SequenceEqual([2, 1]), "default candidate policy creates the evidenced physical spread even in single-page layout");
            state.Preferences.AggressivePairs = false;
            var strict = LayoutEngine.Groups(publication, state, directions, pairs);
            True(strict.All(group => group.Indices.Length == 1), "strict policy does not accept a merely suggested seam");
            state.Preferences.AggressivePairs = true; state.Preferences.Direction = "rtl";
            var reverse = LayoutEngine.Groups(publication, state, directions, pairs);
            Sequence(spread.Indices, reverse.Single(group => group.Indices.Contains(1)).Indices, "candidate physical sides remain independent of reading direction");
        });
        Check("neighboring seam rivals prevent ambiguous automatic joins", () =>
        {
            var publication = Pages(6);
            var state = Settings("single", "rtl");
            var directions = publication.Units.ToDictionary(unit => unit.Id, _ => new SpreadDecision());
            var ambiguous = new Dictionary<int, PairDecision> { [1] = new(1, false, false, .62, Suggested: true), [2] = new(2, false, true, .61, Suggested: true) };
            True(LayoutEngine.Groups(publication, state, directions, ambiguous).All(group => group.Indices.Length == 1), "close competing seams remain separate");
            ambiguous[2] = new(2, false, true, .72, Suggested: true);
            var chosen = LayoutEngine.Groups(publication, state, directions, ambiguous);
            Sequence([3, 2], chosen.Single(group => group.Indices.Contains(2)).Indices, "one clear winning neighbor is selected");
            Sequence(Enumerable.Range(0, 6), chosen.SelectMany(group => group.Indices).Order(), "competing candidate selection consumes every source once");
        });
        Check("candidate joins require upright evidence and preserve manual corrections", () =>
        {
            var publication = Pages(4);
            var state = Settings("single", "ltr");
            var pairs = new Dictionary<int, PairDecision> { [1] = new(1, false, false, .75, Suggested: true) };
            True(LayoutEngine.Groups(publication, state, pairs: pairs).All(group => group.Indices.Length == 1), "missing orientation evidence does not force a seam");
            publication.Units[1].RotationHint = 0; publication.Units[2].RotationHint = 0;
            True(LayoutEngine.Groups(publication, state, pairs: pairs).Any(group => group.Indices.SequenceEqual([1, 2])), "explicit zero-degree publication hints need no redundant OCR");
            state.Overrides[publication.Units[1].Id] = new PageOverride { Standalone = true };
            True(LayoutEngine.Groups(publication, state, pairs: pairs).All(group => group.Indices.Length == 1), "manual standalone rejects candidate join");
            state.Overrides[publication.Units[1].Id] = new PageOverride { JoinNext = true, EarlierOnRight = true };
            Sequence([2, 1], LayoutEngine.Groups(publication, state, pairs: pairs).Single(group => group.Indices.Contains(1)).Indices, "manual physical pair overrides candidate placement");
        });
        Check("ordinary double pages stay distinct from intelligent seamless spreads", () =>
        {
            var publication = Pages(6);
            var state = Settings("double", "rtl");
            state.Preferences.SmartSpreads = false;
            var ordinary = LayoutEngine.Groups(publication, state);
            var pair = ordinary.First(group => group.Indices.Length == 2);
            True(!pair.Spread && pair.Indices.SequenceEqual([2, 1]), "ordinary RTL double pages use separate page layout without an invented seam");
            state.Preferences.Layout = "auto";
            True(LayoutEngine.Groups(publication, state, wideScreen: false).All(group => group.Indices.Length == 1), "auto layout uses single pages on a narrow viewport");
            True(LayoutEngine.Groups(publication, state, wideScreen: true).Any(group => group.Indices.Length == 2 && !group.Spread), "auto layout uses ordinary double pages on a wide viewport");
        });
    }
}
