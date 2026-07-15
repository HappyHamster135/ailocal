namespace AiLocal.Node.Hosting;

/// <summary>
/// Minimal line-based diff (LCS) with no external dependency - enough to
/// show an operator what an agent's file write will change before they
/// approve it. Returns unified-diff-style text with "-" for removed and "+"
/// for added lines (context lines unmarked). Intentionally tiny: this is a
/// safety preview, not a diff-engine.
/// </summary>
public static class LineDiff
{
    public static string Compute(string oldText, string newText)
    {
        var oldLines = SplitLines(oldText);
        var newLines = SplitLines(newText);

        var n = oldLines.Length;
        var m = newLines.Length;
        var dp = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                dp[i, j] = oldLines[i] == newLines[j]
                    ? dp[i + 1, j + 1] + 1
                    : Math.Max(dp[i + 1, j], dp[i, j + 1]);

        var outLines = new List<string>();
        int i2 = 0, j2 = 0;
        while (i2 < n && j2 < m)
        {
            if (oldLines[i2] == newLines[j2])
            {
                outLines.Add("  " + oldLines[i2]);
                i2++; j2++;
            }
            else if (dp[i2 + 1, j2] >= dp[i2, j2 + 1])
            {
                outLines.Add("- " + oldLines[i2]);
                i2++;
            }
            else
            {
                outLines.Add("+ " + newLines[j2]);
                j2++;
            }
        }
        while (i2 < n) { outLines.Add("- " + oldLines[i2]); i2++; }
        while (j2 < m) { outLines.Add("+ " + newLines[j2]); j2++; }

        return string.Join("\n", outLines);
    }

    private static string[] SplitLines(string text) =>
        string.IsNullOrEmpty(text)
            ? []
            : text.Replace("\r\n", "\n").Split('\n');
}
