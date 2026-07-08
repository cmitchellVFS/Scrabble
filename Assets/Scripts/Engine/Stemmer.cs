using System.Collections.Generic;

namespace Shaderpark.Scrabble
{
    /// <summary>
    /// Generates candidate base forms for a word so a definition lookup can fall
    /// back from an inflected form (CATS, RUNNING, HAPPIER) to its lemma.
    /// </summary>
    public static class Stemmer
    {
        public static List<string> Bases(string w)
        {
            List<string> outList = new List<string>();
            int n = w.Length;
            if (n < 3) return outList;

            if (w.EndsWith("IES")) Add(outList, w.Substring(0, n - 3) + "Y");
            if (w.EndsWith("ES")) Add(outList, w.Substring(0, n - 2));
            if (w.EndsWith("VES"))
            {
                Add(outList, w.Substring(0, n - 3) + "F");
                Add(outList, w.Substring(0, n - 3) + "FE");
            }
            if (w.EndsWith("S") && !w.EndsWith("SS")) Add(outList, w.Substring(0, n - 1));
            if (w.EndsWith("IED")) Add(outList, w.Substring(0, n - 3) + "Y");
            if (w.EndsWith("ED"))
            {
                Add(outList, w.Substring(0, n - 2));
                Add(outList, w.Substring(0, n - 1));
                AddDoubled(outList, w.Substring(0, n - 2));
            }
            if (w.EndsWith("ING"))
            {
                Add(outList, w.Substring(0, n - 3));
                Add(outList, w.Substring(0, n - 3) + "E");
                AddDoubled(outList, w.Substring(0, n - 3));
            }
            if (w.EndsWith("IEST")) Add(outList, w.Substring(0, n - 4) + "Y");
            else if (w.EndsWith("EST"))
            {
                Add(outList, w.Substring(0, n - 3));
                Add(outList, w.Substring(0, n - 2));
            }
            if (w.EndsWith("IER")) Add(outList, w.Substring(0, n - 3) + "Y");
            else if (w.EndsWith("ER"))
            {
                Add(outList, w.Substring(0, n - 2));
                Add(outList, w.Substring(0, n - 1));
            }
            if (w.EndsWith("LY")) Add(outList, w.Substring(0, n - 2));
            return outList;
        }

        private static void AddDoubled(List<string> outList, string stem)
        {
            int m = stem.Length;
            if (m >= 2 && stem[m - 1] == stem[m - 2])
            {
                Add(outList, stem.Substring(0, m - 1));
            }
        }

        private static void Add(List<string> outList, string s)
        {
            if (s.Length >= 2 && !outList.Contains(s))
            {
                outList.Add(s);
            }
        }
    }
}
