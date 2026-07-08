namespace Shaderpark.Scrabble
{
    /// <summary>Static Scrabble constants: letter values, tile distribution, premium board.</summary>
    public static class ScrabbleData
    {
        // Point value for each letter A..Z. Blanks are 0.
        public static readonly int[] VALUES = {
            1, 3, 3, 2, 1, 4, 2, 4, 1, 8, 5, 1, 3,
            1, 1, 3, 10, 1, 1, 1, 1, 4, 4, 8, 4, 10
        };

        // How many of each tile A..Z are in the bag.
        public static readonly int[] DIST = {
            9, 2, 2, 4, 12, 2, 3, 2, 9, 1, 1, 4, 2,
            6, 8, 2, 1, 6, 4, 6, 4, 2, 2, 1, 2, 1
        };

        public const int BLANKS = 2;

        // Premium layout, 15 rows of 15 chars.
        //   T = triple word, D = double word, * = centre (double word)
        //   t = triple letter, d = double letter, . = plain
        public static readonly string[] PREMIUM = {
            "T..d...T...d..T",
            ".D...t...t...D.",
            "..D...d.d...D..",
            "d..D...d...D..d",
            "....D.....D....",
            ".t...t...t...t.",
            "..d...d.d...d..",
            "T..d...*...d..T",
            "..d...d.d...d..",
            ".t...t...t...t.",
            "....D.....D....",
            "d..D...d...D..d",
            "..D...d.d...D..",
            ".D...t...t...D.",
            "T..d...T...d..T"
        };

        public static int Value(char upper)
        {
            if (upper >= 'A' && upper <= 'Z')
            {
                return VALUES[upper - 'A'];
            }
            return 0;
        }

        public static char PremiumAt(int r, int c)
        {
            return PREMIUM[r][c];
        }
    }
}
