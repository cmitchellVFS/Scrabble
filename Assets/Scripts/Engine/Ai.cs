using System.Collections.Generic;

namespace Shaderpark.Scrabble
{
    /// <summary>
    /// Chooses a move for the computer opponent. Strength scales with an ELO rating.
    /// </summary>
    public sealed class Ai
    {
        private readonly Dict dict;
        private readonly System.Random rnd;

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
            List<Move> moves = LegalMoves(board, rack);
            if (moves.Count == 0)
            {
                if (bagSize >= 1)
                {
                    int count = System.Math.Min(rack.Count, System.Math.Max(1, bagSize >= 7 ? rack.Count : 1));
                    int[] idx = new int[count];
                    for (int i = 0; i < count; i++) idx[i] = i;
                    return Move.Exchange(idx);
                }
                return Move.Pass();
            }

            moves.Sort(delegate (Move a, Move b) { return b.score - a.score; });

            double skill = SkillFor(elo);
            int n = moves.Count;

            double factor = 0.5 + 0.5 * rnd.NextDouble();
            int index = (int)System.Math.Round((1.0 - skill) * factor * (n - 1));

            if (skill < 0.5)
            {
                int best = moves[0].score;
                int cap = (int)System.Math.Round(best * (0.35 + 0.6 * skill));
                int start = 0;
                while (start < n - 1 && moves[start].score > cap) start++;
                if (index < start) index = start;
            }

            if (index < 0) index = 0;
            if (index >= n) index = n - 1;
            return moves[index];
        }
    }
}
