using System.Collections.Generic;

namespace Shaderpark.Scrabble
{
    /// <summary>
    /// Two-human real-time match orchestration (no AI). Mirrors <see cref="Game"/>'s
    /// rules but keeps both sides symmetric ("A"/"B") since either side's moves may
    /// arrive from a remote player over the network. Runs only on the authoritative
    /// (host) peer; the other peer renders synced state and its own private rack.
    /// </summary>
    public sealed class PvpGame
    {
        public readonly Board board = new Board();
        public readonly Bag bag;
        public readonly List<char> rackA = new List<char>();
        public readonly List<char> rackB = new List<char>();

        public int scoreA;
        public int scoreB;
        public bool turnA = true;
        public bool over;
        public string lastEvent = "";

        private readonly Dict dict;
        private int scoreless;

        public PvpGame(Dict dict, long seed)
        {
            this.dict = dict;
            bag = new Bag(new System.Random((int)seed));
            bag.Fill(rackA, 7);
            bag.Fill(rackB, 7);
        }

        public int BagSize() { return bag.Size(); }

        public List<char> RackOf(bool isA) { return isA ? rackA : rackB; }

        public Board.Validation TrySubmit(bool isA, Move m)
        {
            Board.Validation v = board.ValidateHuman(m, dict);
            if (!v.ok) return v;
            board.Apply(m);
            List<char> rack = RackOf(isA);
            m.RemoveFromRack(rack);
            if (isA) scoreA += v.score; else scoreB += v.score;
            bag.Fill(rack, 7);
            scoreless = 0;
            lastEvent = (isA ? "A" : "B") + " played " + m.word + " for " + v.score;
            if (rack.Count == 0 && bag.IsEmpty()) FinishByGoingOut(isA);
            else turnA = !isA;
            return v;
        }

        public void Pass(bool isA)
        {
            scoreless++;
            lastEvent = (isA ? "A" : "B") + " passed";
            EndIfStalled();
            if (!over) turnA = !isA;
        }

        public bool Exchange(bool isA, int[] indices)
        {
            if (bag.IsEmpty() || indices.Length == 0) return false;
            List<char> rack = RackOf(isA);
            int[] idx = (int[])indices.Clone();
            System.Array.Sort(idx);
            List<char> removed = new List<char>();
            for (int i = idx.Length - 1; i >= 0; i--)
            {
                removed.Add(rack[idx[i]]);
                rack.RemoveAt(idx[i]);
            }
            bag.Fill(rack, 7);
            for (int i = 0; i < removed.Count; i++) bag.PutBack(removed[i]);
            scoreless++;
            lastEvent = (isA ? "A" : "B") + " exchanged " + removed.Count + " tile(s)";
            EndIfStalled();
            if (!over) turnA = !isA;
            return true;
        }

        private void EndIfStalled()
        {
            if (scoreless >= 6)
            {
                scoreA -= Game.RackValue(rackA);
                scoreB -= Game.RackValue(rackB);
                over = true;
            }
        }

        private void FinishByGoingOut(bool wentOutIsA)
        {
            if (wentOutIsA)
            {
                scoreA += Game.RackValue(rackB);
                scoreB -= Game.RackValue(rackB);
            }
            else
            {
                scoreB += Game.RackValue(rackA);
                scoreA -= Game.RackValue(rackA);
            }
            over = true;
        }
    }
}
