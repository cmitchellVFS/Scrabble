using System.Collections.Generic;
using System.Text;

namespace Shaderpark.Scrabble
{
    /// <summary>
    /// The 15x15 board state plus the rules that depend on it: applying a move,
    /// scoring a set of placements, and validating a human placement.
    /// </summary>
    public sealed class Board
    {
        public const int N = 15;
        public const int CENTER = 7;

        private readonly char[][] grid = NewCharGrid();
        private readonly bool[][] blank = NewBoolGrid();

        private static char[][] NewCharGrid()
        {
            char[][] g = new char[N][];
            for (int r = 0; r < N; r++) g[r] = new char[N];
            return g;
        }

        private static bool[][] NewBoolGrid()
        {
            bool[][] g = new bool[N][];
            for (int r = 0; r < N; r++) g[r] = new bool[N];
            return g;
        }

        public bool IsEmptyCell(int r, int c) { return grid[r][c] == (char)0; }
        public char LetterAt(int r, int c) { return grid[r][c]; }
        public bool IsBlankAt(int r, int c) { return blank[r][c]; }

        public bool IsBoardEmpty()
        {
            for (int r = 0; r < N; r++)
                for (int c = 0; c < N; c++)
                    if (grid[r][c] != (char)0) return false;
            return true;
        }

        public char[][] GridCopy()
        {
            char[][] g = new char[N][];
            for (int r = 0; r < N; r++)
            {
                g[r] = new char[N];
                System.Array.Copy(grid[r], g[r], N);
            }
            return g;
        }

        public bool[][] BlankCopy()
        {
            bool[][] b = new bool[N][];
            for (int r = 0; r < N; r++)
            {
                b[r] = new bool[N];
                System.Array.Copy(blank[r], b[r], N);
            }
            return b;
        }

        public void SetCell(int r, int c, char letter, bool isBlank)
        {
            grid[r][c] = letter;
            blank[r][c] = isBlank;
        }

        public void Clear()
        {
            for (int r = 0; r < N; r++)
                for (int c = 0; c < N; c++)
                {
                    grid[r][c] = (char)0;
                    blank[r][c] = false;
                }
        }

        public void Apply(Move m)
        {
            for (int i = 0; i < m.pr.Length; i++)
            {
                grid[m.pr[i]][m.pc[i]] = m.pl[i];
                blank[m.pr[i]][m.pc[i]] = m.pb[i];
            }
        }

        private static bool InBounds(int r, int c)
        {
            return r >= 0 && r < N && c >= 0 && c < N;
        }

        // ------------------------------------------------------------------
        // Scoring
        // ------------------------------------------------------------------

        public int Score(Move m)
        {
            char[][] g = GridCopy();
            bool[][] bl = BlankCopy();
            bool[][] isNew = NewBoolGrid();
            for (int i = 0; i < m.pr.Length; i++)
            {
                g[m.pr[i]][m.pc[i]] = m.pl[i];
                bl[m.pr[i]][m.pc[i]] = m.pb[i];
                isNew[m.pr[i]][m.pc[i]] = true;
            }

            int total = 0;
            bool horizontal = m.horizontal;
            bool single = m.pr.Length == 1;

            if (single)
            {
                total += ScoreWord(g, bl, isNew, m.pr[0], m.pc[0], 0, 1);
                total += ScoreWord(g, bl, isNew, m.pr[0], m.pc[0], 1, 0);
            }
            else if (horizontal)
            {
                int r = m.pr[0];
                total += ScoreWord(g, bl, isNew, r, m.pc[0], 0, 1);
                for (int i = 0; i < m.pr.Length; i++)
                    total += ScoreWord(g, bl, isNew, m.pr[i], m.pc[i], 1, 0);
            }
            else
            {
                int c = m.pc[0];
                total += ScoreWord(g, bl, isNew, m.pr[0], c, 1, 0);
                for (int i = 0; i < m.pr.Length; i++)
                    total += ScoreWord(g, bl, isNew, m.pr[i], m.pc[i], 0, 1);
            }

            if (m.pr.Length == 7)
            {
                total += 50; // bingo
            }
            m.score = total;
            return total;
        }

        private int ScoreWord(char[][] g, bool[][] bl, bool[][] isNew, int r, int c, int dr, int dc)
        {
            int sr = r, sc = c;
            while (InBounds(sr - dr, sc - dc) && g[sr - dr][sc - dc] != (char)0) { sr -= dr; sc -= dc; }
            int er = r, ec = c;
            while (InBounds(er + dr, ec + dc) && g[er + dr][ec + dc] != (char)0) { er += dr; ec += dc; }
            int len = System.Math.Abs(er - sr) + System.Math.Abs(ec - sc) + 1;
            if (len < 2) return 0;

            int wordScore = 0;
            int wordMult = 1;
            int cr = sr, cc = sc;
            for (int i = 0; i < len; i++)
            {
                int val = bl[cr][cc] ? 0 : ScrabbleData.Value(g[cr][cc]);
                if (isNew[cr][cc])
                {
                    char p = ScrabbleData.PremiumAt(cr, cc);
                    if (p == 'd') val *= 2;
                    else if (p == 't') val *= 3;
                    else if (p == 'D' || p == '*') wordMult *= 2;
                    else if (p == 'T') wordMult *= 3;
                }
                wordScore += val;
                cr += dr;
                cc += dc;
            }
            return wordScore * wordMult;
        }

        // ------------------------------------------------------------------
        // Human move validation
        // ------------------------------------------------------------------

        public sealed class Validation
        {
            public bool ok;
            public string error;
            public int score;
            public List<string> words = new List<string>();
        }

        public Validation ValidateHuman(Move m, Dict dict)
        {
            Validation v = new Validation();
            int n = m.pr.Length;
            if (n == 0)
            {
                v.error = "Place at least one tile.";
                return v;
            }
            bool sameRow = true, sameCol = true;
            for (int i = 1; i < n; i++)
            {
                if (m.pr[i] != m.pr[0]) sameRow = false;
                if (m.pc[i] != m.pc[0]) sameCol = false;
            }
            if (!sameRow && !sameCol)
            {
                v.error = "Tiles must be in one row or column.";
                return v;
            }
            bool horizontal = (n == 1) ? true : sameRow;
            m.horizontal = horizontal;

            char[][] g = GridCopy();
            bool[][] isNew = NewBoolGrid();
            for (int i = 0; i < n; i++)
            {
                if (g[m.pr[i]][m.pc[i]] != (char)0)
                {
                    v.error = "Overlapping tiles.";
                    return v;
                }
                g[m.pr[i]][m.pc[i]] = m.pl[i];
                isNew[m.pr[i]][m.pc[i]] = true;
            }

            // Contiguity along the main line (no gaps between placed tiles).
            if (n > 1)
            {
                int lo, hi;
                if (horizontal) { lo = m.pc[0]; } else { lo = m.pr[0]; }
                hi = lo;
                for (int i = 0; i < n; i++)
                {
                    int p = horizontal ? m.pc[i] : m.pr[i];
                    if (p < lo) lo = p;
                    if (p > hi) hi = p;
                }
                int line = horizontal ? m.pr[0] : m.pc[0];
                for (int p = lo; p <= hi; p++)
                {
                    int rr = horizontal ? line : p;
                    int cc = horizontal ? p : line;
                    if (g[rr][cc] == (char)0)
                    {
                        v.error = "Gap in the word.";
                        return v;
                    }
                }
            }

            bool boardEmpty = IsBoardEmpty();
            if (boardEmpty)
            {
                bool coversCenter = false;
                for (int i = 0; i < n; i++)
                    if (m.pr[i] == CENTER && m.pc[i] == CENTER) coversCenter = true;
                if (!coversCenter)
                {
                    v.error = "First word must cover the centre star.";
                    return v;
                }
                if (n < 2)
                {
                    v.error = "First word needs at least two tiles.";
                    return v;
                }
            }
            else
            {
                bool touches = false;
                int[][] d = { new int[] { 1, 0 }, new int[] { -1, 0 }, new int[] { 0, 1 }, new int[] { 0, -1 } };
                for (int i = 0; i < n && !touches; i++)
                {
                    int r = m.pr[i], c = m.pc[i];
                    for (int k = 0; k < 4; k++)
                    {
                        int rr = r + d[k][0], cc = c + d[k][1];
                        if (InBounds(rr, cc) && grid[rr][cc] != (char)0) { touches = true; break; }
                    }
                }
                if (!touches)
                {
                    v.error = "Word must connect to existing tiles.";
                    return v;
                }
            }

            if (n == 1)
            {
                AddWord(v, g, dict, m.pr[0], m.pc[0], 0, 1);
                AddWord(v, g, dict, m.pr[0], m.pc[0], 1, 0);
            }
            else if (horizontal)
            {
                AddWord(v, g, dict, m.pr[0], m.pc[0], 0, 1);
                for (int i = 0; i < n; i++) AddWord(v, g, dict, m.pr[i], m.pc[i], 1, 0);
            }
            else
            {
                AddWord(v, g, dict, m.pr[0], m.pc[0], 1, 0);
                for (int i = 0; i < n; i++) AddWord(v, g, dict, m.pr[i], m.pc[i], 0, 1);
            }
            if (v.error != null) return v;
            if (v.words.Count == 0)
            {
                v.error = "No valid word formed.";
                return v;
            }

            v.score = Score(m);
            v.ok = true;
            return v;
        }

        private void AddWord(Validation v, char[][] g, Dict dict, int r, int c, int dr, int dc)
        {
            if (v.error != null) return;
            int sr = r, sc = c;
            while (InBounds(sr - dr, sc - dc) && g[sr - dr][sc - dc] != (char)0) { sr -= dr; sc -= dc; }
            int er = r, ec = c;
            while (InBounds(er + dr, ec + dc) && g[er + dr][ec + dc] != (char)0) { er += dr; ec += dc; }
            int len = System.Math.Abs(er - sr) + System.Math.Abs(ec - sc) + 1;
            if (len < 2) return;
            StringBuilder sb = new StringBuilder();
            int cr = sr, cc = sc;
            for (int i = 0; i < len; i++)
            {
                sb.Append(g[cr][cc]);
                cr += dr;
                cc += dc;
            }
            string w = sb.ToString();
            if (!dict.Contains(w))
            {
                v.error = "Not a word: " + w;
                return;
            }
            v.words.Add(w);
        }
    }
}
