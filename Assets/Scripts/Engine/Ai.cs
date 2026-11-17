using System.Collections.Generic;

namespace Shaderpark.Scrabble
{
    /// <summary>
    /// Chooses a move for the computer opponent. Strength scales with an ELO rating:
    /// <list type="bullet">
    /// <item>Vocabulary: weaker players only "know" common words (by real-world word
    /// frequency), so a beginner AI plays CAT and HOME, not QOPH and ZOEAE.</item>
    /// <item>Rack leave: stronger players weigh the tiles they keep (blanks and S are
    /// valuable, Q/V/duplicates are a burden), not just this turn's points.</item>
    /// <item>Exchanges: stronger players swap a bad rack instead of taking a poor play.
    /// Exchanging needs at least <see cref="Game.MinBagForExchange"/> tiles in the bag.</item>
    /// </list>
    /// </summary>
    public sealed class Ai
    {
        private readonly Dict dict;
        private readonly System.Random rnd;

        // Word commonness: Zipf frequency × 10 (e.g. CAT = 48) for words common
        // enough to matter; absent = rare. Loaded once at startup from
        // StreamingAssets/wordfreq.tsv. Null = no data (vocabulary not limited).
        public static Dictionary<string, byte> WordFreq;

        // At/above this skill the AI knows every valid word.
        private const double VocabAllKnownSkill = 0.7;   // ~ELO 1560
        // At/above this skill the AI knows every two-letter word (the first
        // thing improving players learn); below it, only everyday ones.
        private const double AllTwosSkill = 0.45;        // ~ELO 1110

        private static readonly HashSet<string> CommonTwos = new HashSet<string>(new string[] {
            "AH", "AM", "AN", "AS", "AT", "AW", "AX", "BE", "BY", "DO", "EH", "GO", "HA", "HE",
            "HI", "HM", "ID", "IF", "IN", "IS", "IT", "LA", "MA", "ME", "MY", "NO", "OF", "OH",
            "ON", "OR", "OW", "OX", "PA", "SO", "TO", "UH", "UM", "UP", "US", "WE", "YA", "YE", "YO"
        });

        public Ai(Dict dict, System.Random rnd)
        {
            this.dict = dict;
            this.rnd = rnd;
        }

        public static double SkillFor(int elo)
        {
            double s = (elo - 300.0) / (2100.0 - 300.0);
            if (s < 0) s = 0;
            if (s > 1) s = 1;
            return s;
        }

        public List<Move> LegalMoves(Board board, List<char> rack)
        {
            char[] tiles = new char[rack.Count];
            for (int i = 0; i < rack.Count; i++) tiles[i] = rack[i];
            List<Move> raw = new MoveGen(dict).Generate(board, tiles);
            HashSet<string> seen = new HashSet<string>();
            List<Move> moves = new List<Move>();
            for (int i = 0; i < raw.Count; i++)
            {
                Move m = raw[i];
                string key = m.Key();
                if (seen.Contains(key)) continue;
                seen.Add(key);
                board.Score(m);
                moves.Add(m);
            }
            return moves;
        }

        public Move ChooseMove(Board board, List<char> rack, int bagSize, int elo)
        {
            double skill = SkillFor(elo);
            bool canExchange = bagSize >= Game.MinBagForExchange && rack.Count > 0;
            List<Move> moves = LegalMoves(board, rack);

            // 1. Vocabulary: keep only moves whose words this player "knows".
            if (moves.Count > 0 && skill < VocabAllKnownSkill && WordFreq != null)
            {
                // A little per-turn jitter: sometimes a word comes to mind, sometimes not.
                double minZipf = MinZipfFor(skill) + (rnd.NextDouble() - 0.5) * 0.6;
                List<Move> known = new List<Move>();
                for (int i = 0; i < moves.Count; i++)
                    if (KnowsAllWords(board, moves[i], skill, minZipf)) known.Add(moves[i]);
                if (known.Count > 0) moves = known;
                else if (canExchange) return Move.Exchange(BestExchange(rack));
                // else: stuck with only unfamiliar words and can't swap -- play anyway.
            }

            if (moves.Count == 0)
            {
                if (canExchange) return Move.Exchange(BestExchange(rack));
                return Move.Pass();
            }

            // 2. Equity = points now + how good the tiles kept are (weighted by skill).
            double lw = LeaveWeight(skill);
            Dictionary<Move, double> equity = new Dictionary<Move, double>(moves.Count);
            for (int i = 0; i < moves.Count; i++)
            {
                Move m = moves[i];
                equity[m] = m.score + lw * LeaveValue(LeaveAfter(rack, m), bagSize);
            }
            moves.Sort(delegate (Move a, Move b) { return equity[b].CompareTo(equity[a]); });

            // 3. Exchange when the best play is poor and swapping keeps a better rack.
            if (canExchange && skill >= 0.5)
            {
                double best = equity[moves[0]];
                int[] swap = BestExchange(rack);
                double keepValue = lw * LeaveValue(KeptAfterExchange(rack, swap), bagSize);
                if (best < 15 && keepValue - best > ExchangeMargin) return Move.Exchange(swap);
            }

            // 4. Strength: stronger players pick nearer the top of the list.
            double pick = PickSkill(skill);
            int n = moves.Count;
            double factor = 0.5 + 0.5 * rnd.NextDouble();
            int index = (int)System.Math.Round((1.0 - pick) * factor * (n - 1));

            if (skill < 0.5)
            {
                // Weak players rarely stumble onto the very best plays: skip moves
                // scoring above a skill-based cap of the best available score.
                // (Uses the real skill, not the vocabulary-compensated one.)
                int bestScore = 0;
                for (int i = 0; i < n; i++) if (moves[i].score > bestScore) bestScore = moves[i].score;
                int cap = (int)System.Math.Round(bestScore * (0.35 + 0.6 * skill));
                int start = 0;
                while (start < n - 1 && moves[start].score > cap) start++;
                if (index < start) index = start;
            }

            if (index < 0) index = 0;
            if (index >= n) index = n - 1;
            return moves[index];
        }

        // ------------------------------------------------------------------
        // Vocabulary
        // ------------------------------------------------------------------

        /// <summary>Minimum Zipf frequency of words a player of this skill knows
        /// (300 ELO: 4.0, 700: 3.2, 1000: 2.6, 1400: 1.8; everything from ~1560).</summary>
        public static double MinZipfFor(double skill)
        {
            return 4.0 - 3.6 * skill;
        }

        /// <summary>
        /// The vocabulary limit already makes weaker players weaker, so they pick
        /// better within the words they do know -- otherwise a given ELO would play
        /// much weaker than before. 0 = no compensation; tuned by self-play so each
        /// ELO keeps roughly its old scoring level.
        /// </summary>
        public static double VocabPickBoost = 0.5;

        private double PickSkill(double skill)
        {
            if (WordFreq == null || skill >= VocabAllKnownSkill) return skill;
            double limit = 1.0 - skill / VocabAllKnownSkill; // 1 at 300 ELO, 0 once all words known
            return skill + (1.0 - skill) * VocabPickBoost * limit;
        }

        /// <summary>Parses wordfreq.tsv ("WORD\tzipf×10" per line).</summary>
        public static Dictionary<string, byte> ParseWordFreq(string text)
        {
            Dictionary<string, byte> d = new Dictionary<string, byte>(80000);
            if (text == null) return d;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int t = line.IndexOf('\t');
                if (t <= 0) continue;
                int v = 0;
                for (int j = t + 1; j < line.Length; j++)
                {
                    char ch = line[j];
                    if (ch < '0' || ch > '9') break;
                    v = v * 10 + (ch - '0');
                }
                d[line.Substring(0, t)] = (byte)System.Math.Min(255, v);
            }
            return d;
        }

        private bool KnowsAllWords(Board board, Move m, double skill, double minZipf)
        {
            List<string> words = FormedWords(board, m);
            for (int i = 0; i < words.Count; i++)
                if (!KnowsWord(words[i], skill, minZipf)) return false;
            return true;
        }

        public static bool KnowsWord(string w, double skill, double minZipf)
        {
            if (skill >= VocabAllKnownSkill || WordFreq == null) return true;
            if (w.Length == 2) return skill >= AllTwosSkill || CommonTwos.Contains(w);
            byte f;
            return WordFreq.TryGetValue(w, out f) && f >= minZipf * 10.0;
        }

        /// <summary>Main word plus every cross-word a move forms (before it is applied).</summary>
        public static List<string> FormedWords(Board board, Move m)
        {
            List<string> words = new List<string>(4);
            if (m.word.Length > 1) words.Add(m.word);
            // Cross-words run perpendicular to the main word through each new tile.
            int dr = m.horizontal ? 1 : 0, dc = m.horizontal ? 0 : 1;
            for (int i = 0; i < m.pr.Length; i++)
            {
                int r = m.pr[i], c = m.pc[i];
                int sr = r, sc = c;
                while (In(sr - dr, sc - dc) && !board.IsEmptyCell(sr - dr, sc - dc)) { sr -= dr; sc -= dc; }
                int er = r, ec = c;
                while (In(er + dr, ec + dc) && !board.IsEmptyCell(er + dr, ec + dc)) { er += dr; ec += dc; }
                if (sr == er && sc == ec) continue; // no cross-word here
                char[] buf = new char[(er - sr) + (ec - sc) + 1];
                int k = 0;
                for (int rr = sr, cc = sc; rr <= er && cc <= ec; rr += dr, cc += dc)
                    buf[k++] = (rr == r && cc == c) ? m.pl[i] : board.LetterAt(rr, cc);
                words.Add(new string(buf));
            }
            return words;
        }

        private static bool In(int r, int c) { return r >= 0 && r < Board.N && c >= 0 && c < Board.N; }

        // ------------------------------------------------------------------
        // Rack leave
        // ------------------------------------------------------------------

        // How much the leave counts, by skill: ignored by beginners, fully by experts.
        public static double LeaveWeight(double skill)
        {
            double w = (skill - 0.4) / 0.5;   // 0 at ~1020 ELO, 1 at ~1920+
            if (w < 0) w = 0;
            if (w > 1) w = 1;
            return w;
        }

        // Rough single-tile leave values (points), in the spirit of standard
        // Scrabble leave tables: blank and S are worth keeping; Q, V, U, W are a burden.
        private static readonly double[] TileLeave = {
            //  A     B     C     D     E     F     G     H     I     J     K     L     M
            0.5, -2.0,  0.0,  0.0,  2.5, -2.0, -2.5,  0.5, -1.0, -2.5, -1.5, -0.5, -0.5,
            //  N     O     P     Q     R     S     T     U     V     W     X     Y     Z
            0.0, -1.5, -1.0, -7.0,  1.5,  7.5,  0.0, -3.0, -5.0, -3.0,  2.0, -1.0,  2.0
        };
        private const double BlankLeave = 24.0;
        private const double ExchangeMargin = 2.0; // exchange must beat the best play by this much

        /// <summary>Heuristic value (in points) of keeping these tiles for next turn.</summary>
        public static double LeaveValue(List<char> leave, int bagSize)
        {
            if (leave.Count == 0) return 0;
            if (bagSize == 0)
            {
                // Endgame: no draws left, tiles kept only count against you.
                double stuck = 0;
                for (int i = 0; i < leave.Count; i++) stuck += ScrabbleData.Value(leave[i]);
                return -stuck;
            }

            int[] count = new int[26];
            int blanks = 0, vowels = 0, letters = 0;
            double v = 0;
            for (int i = 0; i < leave.Count; i++)
            {
                char ch = leave[i];
                if (ch == '?') { blanks++; v += BlankLeave; continue; }
                int k = ch - 'A';
                if (k < 0 || k >= 26) continue;
                count[k]++;
                letters++;
                v += TileLeave[k];
                if (ch == 'A' || ch == 'E' || ch == 'I' || ch == 'O' || ch == 'U') vowels++;
            }

            // Duplicates clog the rack (vowels worst; a second S is still fine-ish).
            for (int k = 0; k < 26; k++)
            {
                if (count[k] < 2) continue;
                char ch = (char)('A' + k);
                bool vowel = ch == 'A' || ch == 'E' || ch == 'I' || ch == 'O' || ch == 'U';
                double per = ch == 'S' ? 2.0 : (vowel ? 4.0 : 3.0);
                v -= (count[k] - 1) * per;
            }

            // Q wants a U.
            if (count['Q' - 'A'] > 0) v += count['U' - 'A'] > 0 ? 3.0 : -4.0;

            // Vowel/consonant balance: ideally ~40% vowels among the letters kept.
            if (letters >= 2)
            {
                double dev = System.Math.Abs(vowels - letters * 0.4);
                if (dev > 0.5) v -= (dev - 0.5) * 3.0;
            }
            return v;
        }

        /// <summary>The rack left after playing a move (tiles not used).</summary>
        public static List<char> LeaveAfter(List<char> rack, Move m)
        {
            List<char> left = new List<char>(rack);
            m.RemoveFromRack(left);
            return left;
        }

        /// <summary>Rack indices to swap so the tiles kept have the best leave value.</summary>
        public static int[] BestExchange(List<char> rack)
        {
            int n = System.Math.Min(rack.Count, 7);
            int bestMask = 0;
            double bestVal = double.NegativeInfinity;
            List<char> keep = new List<char>(7);
            // Each mask bit = keep that tile. Keeping everything isn't an exchange.
            for (int mask = 0; mask < (1 << n) - 1; mask++)
            {
                keep.Clear();
                for (int i = 0; i < n; i++) if ((mask & (1 << i)) != 0) keep.Add(rack[i]);
                double val = LeaveValue(keep, 100);
                if (val > bestVal) { bestVal = val; bestMask = mask; }
            }
            List<int> swap = new List<int>(7);
            for (int i = 0; i < n; i++) if ((bestMask & (1 << i)) == 0) swap.Add(i);
            return swap.ToArray();
        }

        private static List<char> KeptAfterExchange(List<char> rack, int[] swap)
        {
            List<char> keep = new List<char>(rack.Count);
            for (int i = 0; i < rack.Count; i++)
                if (System.Array.IndexOf(swap, i) < 0) keep.Add(rack[i]);
            return keep;
        }
    }
}
