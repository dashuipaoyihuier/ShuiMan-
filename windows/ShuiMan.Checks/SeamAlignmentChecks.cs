using System.IO;
using ShuiMan.Core;

namespace ShuiMan.Checks;

internal static partial class Program
{
    private static void SeamAlignmentChecks(string root)
    {
        Check("automatic seam alignment is opt-in while intelligent pairing and manual seam corrections remain usable", () =>
        {
            var publication = Pages(5);
            var state = Settings("single", "ltr");
            var decisions = publication.Units.ToDictionary(unit => unit.Id, _ => new SpreadDecision());
            var pairs = new Dictionary<int, PairDecision> { [1] = new(1, true, true, .95, .04, 1.08) };
            var original = LayoutEngine.Groups(publication, state, decisions, pairs).Single(group => group.Indices.Contains(1));
            True(!state.Preferences.AutomaticSeamAlignment && state.Preferences.AggressivePairs && original.Spread, "new-book alignment is disabled independently of enabled intelligent pairing");
            Sequence([2, 1], original.Indices, "disabled alignment keeps recognized physical page placement");
            Equal(0.0, original.VerticalOffset, "default automatic spread adds no vertical offset");
            Equal(1.0, original.RightScale, "default automatic spread adds no relative scale");
            state.Preferences.AutomaticSeamAlignment = true;
            var aligned = LayoutEngine.Groups(publication, state, decisions, pairs).Single(group => group.Indices.Contains(1));
            Equal(.04, aligned.VerticalOffset, "explicit opt-in applies the cached vertical correction");
            Equal(1.08, aligned.RightScale, "explicit opt-in applies the cached relative scale");
            state.Preferences.AutomaticSeamAlignment = false;
            state.Overrides[publication.Units[1].Id] = new PageOverride { JoinNext = true, EarlierOnRight = true, PairOffset = -.06, PairScale = .93 };
            var manual = LayoutEngine.Groups(publication, state, decisions, pairs).Single(group => group.Indices.Contains(1));
            Equal(-.06, manual.VerticalOffset, "disabling automatic alignment preserves a manual vertical correction");
            Equal(.93, manual.RightScale, "disabling automatic alignment preserves a manual scale correction");
            Equal(new PairDecision(1, true, true, .95, .04, 1.08), pairs[1], "display preference does not destroy reusable automatic analysis");
        });

        Check("seam alignment migrates missing preferences to off and preserves an explicit saved opt-in across reload and import", () =>
        {
            var directory = Path.Combine(root, "seam-alignment-migration");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "library.json"), """
                [{"Id":"legacy-seam-book","Path":"generated.epub","Title":"原创旧书","Revision":"r1",
                  "Position":22,"Total":40,"LocatorKey":"page-22","Bookmarks":["page-5"],
                  "Preferences":{"SmartSpreads":true,"AutomaticPairs":true,"AggressivePairs":true},
                  "Overrides":{"page-7":{"JoinNext":true,"PairOffset":0.02,"PairScale":1.04}}}]
                """);
            var store = new LibraryStore(directory);
            var migrated = store.Books.Single();
            True(!migrated.Preferences.AutomaticSeamAlignment, "a missing legacy preference does not silently enable automatic alignment");
            True(migrated.Position == 22 && migrated.Bookmarks.Contains("page-5") && migrated.Overrides["page-7"].PairOffset == .02,
                "reading progress and existing manual seam adjustments survive migration");
            migrated.Preferences.AutomaticSeamAlignment = true;
            store.SaveReadingState(migrated);
            store.ImportDiscovered([new SavedBook { Id = migrated.Id, Path = migrated.Path, Revision = "r2" }]);
            var reopened = new LibraryStore(directory).Books.Single();
            True(reopened.Preferences.AutomaticSeamAlignment && reopened.Revision == "r2", "an explicit opt-in survives source refresh and process reload");
            reopened.Preferences.AutomaticSeamAlignment = false;
            store.SaveReadingState(reopened);
            True(!new LibraryStore(directory).Books.Single().Preferences.AutomaticSeamAlignment, "an explicit opt-out is durable too");
        });
    }
}
