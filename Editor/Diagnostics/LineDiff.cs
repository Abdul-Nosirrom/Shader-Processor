using System;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Line-by-line diff between two texts, used to map lines of a processed shader
    /// back to the file they were written in.
    ///
    /// It's the textbook longest-common-subsequence diff: find the longest run of
    /// lines both texts share in the same order, and treat every other line as added
    /// (in a) or removed (in b). The processor mostly inserts lines into what you
    /// wrote, so nearly every one of your lines survives as a match.
    /// </summary>
    public static class LineDiff
    {
        // The table below holds one int per pair of differing lines. Past this size
        // (about 100 MB) the middle section is left unmatched rather than allocated.
        const long MAX_TABLE_CELLS = 25_000_000;

        /// <summary>
        /// For every line of <paramref name="a"/>, the index of the line of
        /// <paramref name="b"/> it matches, or -1 if it has no match. Lines are compared
        /// with leading and trailing whitespace ignored, since the processor re-indents
        /// some of what it copies.
        /// </summary>
        public static int[] Match(string[] a, string[] b)
        {
            int[] result = new int[a.Length];
            for (int i = 0; i < result.Length; i++)
                result[i] = -1;

            // Trimmed once up front: the table compares every pair of lines
            string[] ta = TrimAll(a);
            string[] tb = TrimAll(b);

            // Lines that are equal at the start and end of both texts match directly.
            // This keeps the table small: usually only the middle has changes.
            int start = 0;
            while (start < a.Length && start < b.Length && LinesEqual(ta[start], tb[start]))
            {
                result[start] = start;
                start++;
            }

            int endA = a.Length;
            int endB = b.Length;
            while (endA > start && endB > start && LinesEqual(ta[endA - 1], tb[endB - 1]))
            {
                endA--;
                endB--;
                result[endA] = endB;
            }

            int n = endA - start;
            int m = endB - start;
            if (n == 0 || m == 0 || (long)(n + 1) * (m + 1) > MAX_TABLE_CELLS)
                return result;

            // common[i, j] = length of the longest common subsequence of the a lines
            // from i onward and the b lines from j onward (indices relative to start).
            // Filled from the end so the walk below can go forward.
            int[,] common = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
            {
                for (int j = m - 1; j >= 0; j--)
                {
                    if (LinesEqual(ta[start + i], tb[start + j]))
                        common[i, j] = common[i + 1, j + 1] + 1;
                    else
                        common[i, j] = Math.Max(common[i + 1, j], common[i, j + 1]);
                }
            }

            // Walk forward: take a match whenever the lines are equal, otherwise skip
            // the line whose removal keeps the longer common subsequence
            int ai = 0;
            int bi = 0;
            while (ai < n && bi < m)
            {
                if (LinesEqual(ta[start + ai], tb[start + bi]))
                {
                    result[start + ai] = start + bi;
                    ai++;
                    bi++;
                }
                else if (common[ai + 1, bi] >= common[ai, bi + 1])
                {
                    ai++;
                }
                else
                {
                    bi++;
                }
            }

            return result;
        }

        /// <summary>Split text into lines, accepting both LF and CRLF.</summary>
        public static string[] SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text))
                return new string[0];

            return text.Replace("\r\n", "\n").Split('\n');
        }

        static string[] TrimAll(string[] lines)
        {
            var trimmed = new string[lines.Length];
            for (int i = 0; i < lines.Length; i++)
                trimmed[i] = lines[i].Trim();
            return trimmed;
        }

        static bool LinesEqual(string x, string y)
        {
            return string.Equals(x, y, StringComparison.Ordinal);
        }
    }
}
