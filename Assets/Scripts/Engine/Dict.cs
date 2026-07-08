using System.Collections.Generic;

namespace Shaderpark.Scrabble
{
    /// <summary>
    /// A compact prefix trie for the Scrabble word list, built incrementally from a
    /// pre-sorted, upper-cased word stream so peak memory stays low.
    /// </summary>
    public sealed class Dict
    {
        public sealed class Node
        {
            public readonly char[] keys;
            public readonly Node[] kids;
            public readonly bool term;

            public Node(char[] keys, Node[] kids, bool term)
            {
                this.keys = keys;
                this.kids = kids;
                this.term = term;
            }

            public Node Child(char c)
            {
                char[] k = keys;
                for (int i = 0; i < k.Length; i++)
                {
                    if (k[i] == c)
                    {
                        return kids[i];
                    }
                }
                return null;
            }
        }

        // Mutable builder node used only during construction.
        private sealed class B
        {
            public readonly List<char> keys = new List<char>(2);
            public readonly List<object> kids = new List<object>(2); // B (open) or Node (done)
            public bool term;
        }

        private readonly Node root;
        private readonly int wordCount;

        private Dict(Node root, int count)
        {
            this.root = root;
            this.wordCount = count;
        }

        public Node Root()
        {
            return root;
        }

        public int Size()
        {
            return wordCount;
        }

        public bool Contains(string word)
        {
            Node n = root;
            for (int i = 0; i < word.Length; i++)
            {
                n = n.Child(word[i]);
                if (n == null)
                {
                    return false;
                }
            }
            return n.term;
        }

        private static Node Compact(B b)
        {
            int n = b.keys.Count;
            char[] keys = new char[n];
            Node[] kids = new Node[n];
            for (int i = 0; i < n; i++)
            {
                keys[i] = b.keys[i];
                kids[i] = (Node)b.kids[i]; // children finalised deepest-first
            }
            return new Node(keys, kids, b.term);
        }

        /// <summary>Build from an ordered sequence of sorted, upper-case words.</summary>
        public static Dict Build(IEnumerable<string> lines)
        {
            List<B> path = new List<B>();
            path.Add(new B()); // root at depth 0
            string prev = "";
            int count = 0;

            foreach (string line in lines)
            {
                string w = line.Trim();
                if (w.Length == 0)
                {
                    continue;
                }
                int common = CommonPrefix(prev, w);
                // Finalise nodes deeper than the shared prefix (deepest first).
                for (int depth = prev.Length; depth > common; depth--)
                {
                    B done = path[depth];
                    path.RemoveAt(depth);
                    B parent = path[depth - 1];
                    parent.kids[parent.kids.Count - 1] = Compact(done);
                }
                // Extend the path with the new suffix.
                for (int i = common; i < w.Length; i++)
                {
                    B parent = path[i];
                    B node = new B();
                    parent.keys.Add(w[i]);
                    parent.kids.Add(node);
                    path.Add(node);
                }
                path[w.Length].term = true;
                prev = w;
                count++;
            }
            for (int depth = prev.Length; depth > 0; depth--)
            {
                B done = path[depth];
                path.RemoveAt(depth);
                B parent = path[depth - 1];
                parent.kids[parent.kids.Count - 1] = Compact(done);
            }
            return new Dict(Compact(path[0]), count);
        }

        private static int CommonPrefix(string a, string b)
        {
            int n = a.Length < b.Length ? a.Length : b.Length;
            int i = 0;
            while (i < n && a[i] == b[i])
            {
                i++;
            }
            return i;
        }
    }
}
