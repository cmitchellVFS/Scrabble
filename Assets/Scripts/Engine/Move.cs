using System.Collections.Generic;
using System.Text;

namespace Shaderpark.Scrabble
{
    /// <summary>A candidate or played move: a set of tile placements, or a pass/exchange.</summary>
    public sealed class Move
    {
        public const int NORMAL = 0;
        public const int PASS = 1;
        public const int EXCHANGE = 2;

        public int type = NORMAL;
        public bool horizontal = true;
        public string word = "";
        public int score;

        // Parallel arrays describing each newly placed tile.
        public int[] pr = new int[0];
        public int[] pc = new int[0];
        public char[] pl = new char[0];   // assigned letter (upper case)
        public bool[] pb = new bool[0];   // is a blank tile

        // For EXCHANGE: rack indices to swap out.
        public int[] exchange = new int[0];

        public int TilesPlaced()
        {
            return pr.Length;
        }

        /// <summary>Removes the tiles this move used (by letter, blanks as '?') from a rack.</summary>
        public void RemoveFromRack(List<char> rack)
        {
            for (int i = 0; i < pr.Length; i++)
            {
                char want = pb[i] ? '?' : pl[i];
                int idx = rack.IndexOf(want);
                if (idx >= 0) rack.RemoveAt(idx);
            }
        }

        public static Move Pass()
        {
            Move m = new Move();
            m.type = PASS;
            return m;
        }

        public static Move Exchange(int[] idx)
        {
            Move m = new Move();
            m.type = EXCHANGE;
            m.exchange = idx;
            return m;
        }

        /// <summary>Stable key of the placement set, to de-duplicate equivalent moves.</summary>
        public string Key()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < pr.Length; i++)
            {
                sb.Append(pr[i]).Append(',').Append(pc[i]).Append('=')
                    .Append(pl[i]).Append(pb[i] ? 'b' : '.').Append(';');
            }
            return sb.ToString();
        }
    }
}
