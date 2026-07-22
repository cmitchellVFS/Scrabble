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
            ScoreBreakdown b = Explain(m);
            m.score = b.Total();
            return m.score;
        }

        // Detailed, ordered breakdown of a move's score for the scoring
        // animation. Mirrors Score() exactly: same words, same letter (DL/TL)
        // and word (DW/TW) multipliers, same bingo bonus -- Score() is defined
        // in terms of it, so the two can never diverge.
        public ScoreBreakdown Explain(Move m)
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

            ScoreBreakdown b = new ScoreBreakdown();
            bool horizontal = m.horizontal;
            bool single = m.pr.Length == 1;

            if (single)
            {
                b.Add(ExplainWord(g, bl, isNew, m.pr[0], m.pc[0], 0, 1));
                b.Add(ExplainWord(g, bl, isNew, m.pr[0], m.pc[0], 1, 0));
            }
            else if (horizontal)
            {
                b.Add(ExplainWord(g, bl, isNew, m.pr[0], m.pc[0], 0, 1));
                for (int i = 0; i < m.pr.Length; i++)
                    b.Add(ExplainWord(g, bl, isNew, m.pr[i], m.pc[i], 1, 0));
            }
            else
            {
                b.Add(ExplainWord(g, bl, isNew, m.pr[0], m.pc[0], 1, 0));
                for (int i = 0; i < m.pr.Length; i++)
                    b.Add(ExplainWord(g, bl, isNew, m.pr[i], m.pc[i], 0, 1));
            }

            b.bingo = m.pr.Length == 7;
            return b;
        }

        private WordScore ExplainWord(char[][] g, bool[][] bl, bool[][] isNew, int r, int c, int dr, int dc)
        {
            int sr = r, sc = c;
            while (InBounds(sr - dr, sc - dc) && g[sr - dr][sc - dc] != (char)0) { sr -= dr; sc -= dc; }
            int er = r, ec = c;
            while (InBounds(er + dr, ec + dc) && g[er + dr][ec + dc] != (char)0) { er += dr; ec += dc; }
            int len = System.Math.Abs(er - sr) + System.Math.Abs(ec - sc) + 1;
            if (len < 2) return null;

            WordScore ws = new WordScore();
            int cr = sr, cc = sc;
            for (int i = 0; i < len; i++)
            {
                int val = bl[cr][cc] ? 0 : ScrabbleData.Value(g[cr][cc]);
                int lm = 1;
                if (isNew[cr][cc])
                {
                    char p = ScrabbleData.PremiumAt(cr, cc);
                    if (p == 'd') lm = 2;
                    else if (p == 't') lm = 3;
                    else if (p == 'D' || p == '*') ws.wordMult *= 2;
                    else if (p == 'T') ws.wordMult *= 3;
                }
                ws.letters.Add(new LetterScore(cr, cc, val, lm, isNew[cr][cc]));
                cr += dr;
                cc += dc;
            }
            return ws;
        }

        // ---- Score breakdown types (consumed by the scoring animation) ----

        /// <summary>One tile within a scored word.</summary>
        public sealed class LetterScore
        {
            public readonly int r, c;
            public readonly int baseVal;    // face value, 0 for a blank
            public readonly int letterMult; // 1/2/3 -- DL/TL on a newly-placed tile
            public readonly bool isNew;     // placed by this move (vs already on the board)
            public LetterScore(int r, int c, int baseVal, int letterMult, bool isNew)
            {
                this.r = r; this.c = c; this.baseVal = baseVal; this.letterMult = letterMult; this.isNew = isNew;
            }
        }

        /// <summary>One word formed by a move (main word or a cross word).</summary>
        public sealed class WordScore
        {
            public readonly List<LetterScore> letters = new List<LetterScore>();
            public int wordMult = 1; // product of DW/TW multipliers on newly-placed tiles

            public int LetterSum()
            {
                int s = 0;
                for (int i = 0; i < letters.Count; i++) s += letters[i].baseVal * letters[i].letterMult;
                return s;
            }
            public int Subtotal() { return LetterSum() * wordMult; }
        }

        /// <summary>All words formed by a move, plus the bingo flag.</summary>
        public sealed class ScoreBreakdown
        {
            public readonly List<WordScore> words = new List<WordScore>();
            public bool bingo;
            public void Add(WordScore w) { if (w != null) words.Add(w); }
            public int Total()
            {
                int t = 0;
                for (int i = 0; i < words.Count; i++) t += words[i].Subtotal();
                if (bingo) t += 50;
                return t;
            }
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
