using System.Collections.Generic;
using System.Text;

namespace Shaderpark.Scrabble
{
    /// <summary>
    /// Generates every legal move for a rack on a board, using the classic
    /// anchor + cross-check + left-part/extend-right approach (Appel &amp; Jacobson).
    /// </summary>
    public sealed class MoveGen
    {
        private readonly Dict dict;

        private char[][] g;              // 0 = empty
        private bool transposed;
        private bool[][] anchor;
        private bool[][][] cross;        // cross[r][c][0..25] allowed letters (null = all)
        private readonly int[] rack = new int[27]; // 0..25 letters, 26 = blanks
        private List<Move> outList;

        private readonly int[] plColArr = new int[15];
        private readonly int[] plRowArr = new int[15];
        private readonly char[] plLetArr = new char[15];
        private readonly bool[] plBlkArr = new bool[15];
        private int plCount;

        private readonly char[] pfLet = new char[15];
        private readonly bool[] pfBlk = new bool[15];

        public MoveGen(Dict dict)
        {
            this.dict = dict;
        }

        public List<Move> Generate(Board board, char[] rackTiles)
        {
            outList = new List<Move>();
            Run(board.GridCopy(), false, rackTiles);
            Run(Transpose(board.GridCopy()), true, rackTiles);
            return outList;
        }

        private char[][] Transpose(char[][] src)
        {
            int n = Board.N;
            char[][] t = new char[n][];
            for (int i = 0; i < n; i++) t[i] = new char[n];
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                    t[c][r] = src[r][c];
            return t;
        }

        private void Run(char[][] grid, bool transposed, char[] rackTiles)
        {
            this.g = grid;
            this.transposed = transposed;
            for (int i = 0; i < 27; i++) rack[i] = 0;
            for (int i = 0; i < rackTiles.Length; i++)
            {
                char ch = rackTiles[i];
                if (ch == '?') rack[26]++;
                else rack[ch - 'A']++;
            }
            ComputeAnchors();
            ComputeCross();

            int n = Board.N;
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    if (!anchor[r][c]) continue;
                    plCount = 0;
                    if (c > 0 && g[r][c - 1] != (char)0)
                    {
                        int k = c - 1;
                        while (k > 0 && g[r][k - 1] != (char)0) k--;
                        Dict.Node node = dict.Root();
                        bool okPrefix = true;
                        for (int j = k; j <= c - 1; j++)
                        {
                            node = node.Child(g[r][j]);
                            if (node == null) { okPrefix = false; break; }
                        }
                        if (okPrefix) ExtendRight(node, r, c, c, k);
                    }
                    else
                    {
                        int limit = 0;
                        int k = c - 1;
                        while (k >= 0 && g[r][k] == (char)0 && !anchor[r][k]) { limit++; k--; }
                        LeftPart(dict.Root(), r, c, limit, 0);
                    }
                }
            }
        }

        private void ComputeAnchors()
        {
            int n = Board.N;
            anchor = new bool[n][];
            for (int i = 0; i < n; i++) anchor[i] = new bool[n];
            bool empty = true;
            for (int r = 0; r < n && empty; r++)
                for (int c = 0; c < n; c++)
                    if (g[r][c] != (char)0) { empty = false; break; }
            if (empty)
            {
                anchor[Board.CENTER][Board.CENTER] = true;
                return;
            }
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    if (g[r][c] != (char)0) continue;
                    bool adj = (r > 0 && g[r - 1][c] != (char)0)
                            || (r < n - 1 && g[r + 1][c] != (char)0)
                            || (c > 0 && g[r][c - 1] != (char)0)
                            || (c < n - 1 && g[r][c + 1] != (char)0);
                    anchor[r][c] = adj;
                }
            }
        }

        private void ComputeCross()
        {
            int n = Board.N;
            cross = new bool[n][][];
            for (int i = 0; i < n; i++) cross[i] = new bool[n][];
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    if (g[r][c] != (char)0) continue;
                    bool has = (r > 0 && g[r - 1][c] != (char)0) || (r < n - 1 && g[r + 1][c] != (char)0);
                    if (!has) continue; // null => all letters allowed

                    StringBuilder up = new StringBuilder();
                    int k = r - 1;
                    while (k >= 0 && g[k][c] != (char)0) { up.Insert(0, g[k][c]); k--; }
                    StringBuilder down = new StringBuilder();
                    k = r + 1;
                    while (k < n && g[k][c] != (char)0) { down.Append(g[k][c]); k++; }

                    bool[] allowed = new bool[26];
                    string upS = up.ToString();
                    string downS = down.ToString();
                    for (int L = 0; L < 26; L++)
                    {
                        string w = upS + (char)('A' + L) + downS;
                        allowed[L] = dict.Contains(w);
                    }
                    cross[r][c] = allowed;
                }
            }
        }

        private bool CrossAllows(int r, int c, int letter)
        {
            bool[] a = cross[r][c];
            return a == null || a[letter];
        }

        private void LeftPart(Dict.Node node, int r, int anchorCol, int limit, int prefixLen)
        {
            StartExtend(node, r, anchorCol, prefixLen);
            if (limit <= 0) return;
            char[] keys = node.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                int letter = keys[i] - 'A';
                Dict.Node child = node.kids[i];
                if (rack[letter] > 0)
                {
                    rack[letter]--;
                    pfLet[prefixLen] = keys[i];
                    pfBlk[prefixLen] = false;
                    LeftPart(child, r, anchorCol, limit - 1, prefixLen + 1);
                    rack[letter]++;
                }
                if (rack[26] > 0)
                {
                    rack[26]--;
                    pfLet[prefixLen] = keys[i];
                    pfBlk[prefixLen] = true;
                    LeftPart(child, r, anchorCol, limit - 1, prefixLen + 1);
                    rack[26]++;
                }
            }
        }

        private void StartExtend(Dict.Node node, int r, int anchorCol, int prefixLen)
        {
            int startCol = anchorCol - prefixLen;
            plCount = 0;
            for (int i = 0; i < prefixLen; i++)
            {
                Push(r, startCol + i, pfLet[i], pfBlk[i]);
            }
            ExtendRight(node, r, anchorCol, anchorCol, startCol);
            plCount = 0;
        }

        private void ExtendRight(Dict.Node node, int r, int col, int anchorCol, int startCol)
        {
            int n = Board.N;
            if (col >= n)
            {
                MaybeRecord(node, r, col - 1, anchorCol, startCol);
                return;
            }
            char here = g[r][col];
            if (here == (char)0)
            {
                MaybeRecord(node, r, col - 1, anchorCol, startCol);
                char[] keys = node.keys;
                for (int i = 0; i < keys.Length; i++)
                {
                    int letter = keys[i] - 'A';
                    if (!CrossAllows(r, col, letter)) continue;
                    Dict.Node child = node.kids[i];
                    if (rack[letter] > 0)
                    {
                        rack[letter]--;
                        Push(r, col, keys[i], false);
                        ExtendRight(child, r, col + 1, anchorCol, startCol);
                        Pop();
                        rack[letter]++;
                    }
                    if (rack[26] > 0)
                    {
                        rack[26]--;
                        Push(r, col, keys[i], true);
                        ExtendRight(child, r, col + 1, anchorCol, startCol);
                        Pop();
                        rack[26]++;
                    }
                }
            }
            else
            {
                Dict.Node child = node.Child(here);
                if (child != null)
                {
                    ExtendRight(child, r, col + 1, anchorCol, startCol);
                }
            }
        }

        private void MaybeRecord(Dict.Node node, int r, int endCol, int anchorCol, int startCol)
        {
            if (!node.term) return;
            if (plCount == 0) return;
            if (endCol < anchorCol) return;
            if (endCol + 1 < Board.N && g[r][endCol + 1] != (char)0) return;

            Move m = new Move();
            int k = plCount;
            m.pr = new int[k];
            m.pc = new int[k];
            m.pl = new char[k];
            m.pb = new bool[k];

            int[] order = new int[k];
            for (int i = 0; i < k; i++) order[i] = i;
            for (int i = 1; i < k; i++)
            {
                int oi = order[i];
                int j = i - 1;
                while (j >= 0 && plColArr[order[j]] > plColArr[oi])
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = oi;
            }

            StringBuilder word = new StringBuilder();
            for (int cc = startCol; cc <= endCol; cc++)
            {
                word.Append(g[r][cc] != (char)0 ? g[r][cc] : FindPlaced(r, cc));
            }
            for (int i = 0; i < k; i++)
            {
                int idx = order[i];
                int rr = plRowArr[idx];
                int cc = plColArr[idx];
                if (transposed) { m.pr[i] = cc; m.pc[i] = rr; }
                else { m.pr[i] = rr; m.pc[i] = cc; }
                m.pl[i] = plLetArr[idx];
                m.pb[i] = plBlkArr[idx];
            }
            m.horizontal = !transposed;
            m.word = word.ToString();
            outList.Add(m);
        }

        private char FindPlaced(int r, int c)
        {
            for (int i = 0; i < plCount; i++)
            {
                if (plRowArr[i] == r && plColArr[i] == c) return plLetArr[i];
            }
            return '?';
        }

        private void Push(int r, int c, char letter, bool blank)
        {
            plRowArr[plCount] = r;
            plColArr[plCount] = c;
            plLetArr[plCount] = letter;
            plBlkArr[plCount] = blank;
            plCount++;
        }

        private void Pop()
        {
            plCount--;
        }
    }
}
