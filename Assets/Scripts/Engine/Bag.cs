using System.Collections.Generic;

namespace Shaderpark.Scrabble
{
    /// <summary>The tile bag. Blanks are represented by '?'.</summary>
    public sealed class Bag
    {
        private readonly List<char> tiles = new List<char>(100);
        private readonly System.Random rnd;

        public Bag(System.Random rnd)
        {
            this.rnd = rnd;
            for (int i = 0; i < 26; i++)
                for (int j = 0; j < ScrabbleData.DIST[i]; j++)
                    tiles.Add((char)('A' + i));
            for (int j = 0; j < ScrabbleData.BLANKS; j++)
                tiles.Add('?');
        }

        public int Size() { return tiles.Count; }
        public bool IsEmpty() { return tiles.Count == 0; }

        public char Draw()
        {
            int i = rnd.Next(tiles.Count);
            char c = tiles[i];
            tiles.RemoveAt(i);
            return c;
        }

        public void Fill(List<char> rack, int target)
        {
            while (rack.Count < target && tiles.Count > 0)
            {
                rack.Add(Draw());
            }
        }

        public void PutBack(char tile)
        {
            tiles.Add(tile);
        }

        public List<char> Contents()
        {
            return new List<char>(tiles);
        }

        public void SetContents(List<char> t)
        {
            tiles.Clear();
            tiles.AddRange(t);
        }
    }
}
