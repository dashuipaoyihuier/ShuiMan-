using ShuiMan.Core;

namespace ShuiMan.Checks;

internal static partial class Program
{
    private static void PairSamplingChecks()
    {
        const int width = 720, height = 1080;
        double[] Scan(bool right, bool unrelated = false) => Enumerable.Range(0, width * height).Select(index =>
        {
            int x = index % width, y = index / width;
            double phase = (x + (right ? width : 0)) * .008;
            double row = unrelated ? y * 1.31 + 157 : y;
            double drawing = .50 + .17 * Math.Sin(row * .049 + phase) +
                .15 * Math.Sin(row * .117 + phase * .8) + .13 * Math.Cos(row * .189 - phase * 1.2);
            // Original deterministic scan tones have a different phase on the two halves.
            // A four-point downsample aliases them into false edge structure.
            double dots = .45 * Math.Sin(y * 1.1 + (right ? 2.1 : 0) + x * .004);
            return Math.Clamp(drawing + dots + .06 * Math.Sin((x + (right ? width : 0)) * .013), 0, 1);
        }).ToArray();
        Check("scan-tone anti-aliasing restores continuous artwork without lowering seam thresholds", () =>
        {
            var left = Scan(false); var right = Scan(true);
            var decision = PairAnalyzer.AnalyzePixels(left, width, height, right, width, height);
            True(decision.Automatic && !decision.Swapped && decision.MatchingBands >= 5,
                $"underlying rich linework remains a strong physical seam: {decision}");
            var reversed = PairAnalyzer.AnalyzePixels(right, width, height, left, width, height);
            True(reversed.Automatic && reversed.Swapped, "scan noise cannot invert the physical placement");
        });
        Check("shared scan dots do not join unrelated original artwork", () =>
        {
            var decision = PairAnalyzer.AnalyzePixels(Scan(false), width, height, Scan(true, true), width, height);
            True(!decision.Suggested, $"same screen-tone frequency is insufficient evidence: {decision}");
        });
    }
}
