using System.Collections.Generic;
using System.Text;

namespace Shaderpark.Scrabble
{
    /// <summary>Two-player game orchestration: human vs ELO-scaled AI.</summary>
    public sealed class Game
    {
        public readonly Board board = new Board();
        public readonly Bag bag;
        public readonly List<char> humanRack = new List<char>();
        public readonly List<char> aiRack = new List<char>();

        public int humanScore;
        public int aiScore;
        public bool humanTurn = true;
        public bool over;
        public int elo;
        public string lastEvent = "";

        private readonly Dict dict;
        private readonly Ai ai;
        private readonly System.Random rnd;
        private int scoreless;

        public Game(Dict dict, int elo, long seed)
        {
            this.dict = dict;
            this.elo = elo;
            this.rnd = new System.Random((int)seed);
            this.ai = new Ai(dict, rnd);
            this.bag = new Bag(rnd);
            bag.Fill(humanRack, 7);
            bag.Fill(aiRack, 7);
        }

        public int BagSize() { return bag.Size(); }

        public Board.Validation TrySubmitHuman(Move m)
        {
            Board.Validation v = board.ValidateHuman(m, dict);
            if (!v.ok) return v;
            board.Apply(m);
            m.RemoveFromRack(humanRack);
            humanScore += v.score;
            bag.Fill(humanRack, 7);
            scoreless = 0;
            lastEvent = "You played " + m.word + " for " + v.score;
            if (humanRack.Count == 0 && bag.IsEmpty()) FinishByGoingOut(true);
            else humanTurn = false;
            return v;
        }

        public void PassHuman()
        {
            scoreless++;
            lastEvent = "You passed";
            EndIfStalled();
            if (!over) humanTurn = false;
        }

        public bool ExchangeHuman(int[] indices)
        {
            if (bag.IsEmpty() || indices.Length == 0) return false;
            int[] idx = (int[])indices.Clone();
            System.Array.Sort(idx);
            List<char> removed = new List<char>();
            for (int i = idx.Length - 1; i >= 0; i--)
            {
                removed.Add(humanRack[idx[i]]);
                humanRack.RemoveAt(idx[i]);
            }
            bag.Fill(humanRack, 7);
            for (int i = 0; i < removed.Count; i++) bag.PutBack(removed[i]);
            scoreless++;
            lastEvent = "You exchanged " + removed.Count + " tile(s)";
            EndIfStalled();
            if (!over) humanTurn = false;
            return true;
        }

        public Move ChooseAiMove()
        {
            return ai.ChooseMove(board, aiRack, bag.Size(), elo);
        }

        public string DoAiTurn()
        {
            return CommitAiMove(ChooseAiMove());
        }

        public string CommitAiMove(Move m)
        {
            string desc;
            if (m.type == Move.PASS)
            {
                scoreless++;
                desc = "AI passed";
            }
            else if (m.type == Move.EXCHANGE)
            {
                int[] idx = (int[])m.exchange.Clone();
                System.Array.Sort(idx);
                List<char> removed = new List<char>();
                for (int i = idx.Length - 1; i >= 0; i--)
                {
                    if (idx[i] < aiRack.Count)
                    {
                        removed.Add(aiRack[idx[i]]);
                        aiRack.RemoveAt(idx[i]);
                    }
                }
                bag.Fill(aiRack, 7);
                for (int i = 0; i < removed.Count; i++) bag.PutBack(removed[i]);
                scoreless++;
                desc = "AI exchanged " + removed.Count + " tile(s)";
            }
            else
            {
                board.Score(m);
                board.Apply(m);
                m.RemoveFromRack(aiRack);
                aiScore += m.score;
                bag.Fill(aiRack, 7);
                scoreless = 0;
                desc = "AI played " + m.word + " for " + m.score;
            }
            lastEvent = desc;
            if (m.type == Move.NORMAL && aiRack.Count == 0 && bag.IsEmpty())
            {
                FinishByGoingOut(false);
            }
            else
            {
                EndIfStalled();
                if (!over) humanTurn = true;
            }
            return desc;
        }

        private void EndIfStalled()
        {
            if (scoreless >= 6)
            {
                humanScore -= RackValue(humanRack);
                aiScore -= RackValue(aiRack);
                over = true;
            }
        }

        private void FinishByGoingOut(bool humanWentOut)
        {
            if (humanWentOut)
            {
                humanScore += RackValue(aiRack);
                aiScore -= RackValue(aiRack);
            }
            else
            {
                aiScore += RackValue(humanRack);
                humanScore -= RackValue(humanRack);
            }
            over = true;
        }

        public static int RackValue(List<char> rack)
        {
            int v = 0;
            for (int i = 0; i < rack.Count; i++)
            {
                char ch = rack[i];
                if (ch != '?') v += ScrabbleData.Value(ch);
            }
            return v;
        }

        // ------------------------------------------------------------------
        // Persistence (simple line-based text format)
        // ------------------------------------------------------------------

        public string Save()
        {
            StringBuilder grid = new StringBuilder();
            StringBuilder blanks = new StringBuilder();
            for (int r = 0; r < Board.N; r++)
            {
                for (int c = 0; c < Board.N; c++)
                {
                    char ch = board.LetterAt(r, c);
                    grid.Append(ch == (char)0 ? '.' : ch);
                    blanks.Append(board.IsBlankAt(r, c) ? '1' : '0');
                }
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("V1\n")
                .Append(elo).Append('\n')
                .Append(humanScore).Append('\n')
                .Append(aiScore).Append('\n')
                .Append(humanTurn ? 1 : 0).Append('\n')
                .Append(over ? 1 : 0).Append('\n')
                .Append(scoreless).Append('\n')
                .Append(grid).Append('\n')
                .Append(blanks).Append('\n')
                .Append(RackString(humanRack)).Append('\n')
                .Append(RackString(aiRack)).Append('\n')
                .Append(RackString(bag.Contents())).Append('\n')
                .Append(lastEvent == null ? "" : lastEvent.Replace('\n', ' '));
            return sb.ToString();
        }

        public static Game Restore(Dict dict, string data)
        {
            try
            {
                string[] ln = data.Split('\n');
                if (ln.Length < 12 || ln[0] != "V1") return null;
                int elo = int.Parse(ln[1]);
                Game g = new Game(dict, elo, System.DateTime.Now.Ticks);
                g.humanScore = int.Parse(ln[2]);
                g.aiScore = int.Parse(ln[3]);
                g.humanTurn = ln[4] == "1";
                g.over = ln[5] == "1";
                g.scoreless = int.Parse(ln[6]);
                string grid = ln[7];
                string blanks = ln[8];
                g.board.Clear();
                for (int i = 0; i < Board.N * Board.N && i < grid.Length; i++)
                {
                    char ch = grid[i];
                    if (ch != '.')
                    {
                        g.board.SetCell(i / Board.N, i % Board.N, ch, blanks[i] == '1');
                    }
                }
                g.humanRack.Clear();
                FillRack(g.humanRack, ln[9]);
                g.aiRack.Clear();
                FillRack(g.aiRack, ln[10]);
                List<char> bagTiles = new List<char>();
                FillRack(bagTiles, ln[11]);
                g.bag.SetContents(bagTiles);
                g.lastEvent = ln.Length > 12 ? ln[12] : "";
                return g;
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        private static string RackString(List<char> rack)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < rack.Count; i++) sb.Append(rack[i]);
            return sb.ToString();
        }

        private static void FillRack(List<char> rack, string s)
        {
            for (int i = 0; i < s.Length; i++) rack.Add(s[i]);
        }
    }
}
