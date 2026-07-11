using System.Collections.Generic;
using System.Text;
using Coherence;
using Coherence.Common;
using Coherence.Connection;
using Coherence.Toolkit;
using UnityEngine;

namespace Shaderpark.Scrabble
{
    /// <summary>
    /// Networked companion to a live PvP match. Lives on the same GameObject as a
    /// CoherenceSync. The room creator ("Host", side A) holds State Authority and
    /// runs the authoritative <see cref="PvpGame"/> simulation locally; the joining
    /// player ("Guest", side B) holds Input Authority and only renders synced
    /// public state plus its own privately-delivered rack.
    /// </summary>
    /// <remarks>
    /// Coherence syncs [Sync] fields to every observer of the entity, so rack
    /// contents are deliberately never [Sync]'d -- only the host's own process
    /// ever sees both racks. The guest's rack is pushed to it out-of-band via a
    /// Command targeted at the entity's Input Authority (see <see cref="CmdReceiveRack"/>).
    /// </remarks>
    public class ScrabbleNetworkMatch : MonoBehaviour
    {
        // ---- Public/shared synced state (visible to both players) ----
        [Sync] public string packedLetters = "";
        [Sync] public string packedBlanks = "";
        [Sync] public int scoreA;
        [Sync] public int scoreB;
        [Sync] public bool turnA = true;
        [Sync] public bool over;
        [Sync] public int bagCount;
        [Sync] public string lastEvent = "";
        [Sync] public bool matchStarted;
        [Sync] public string lastError = "";
        [Sync] public int errorSeq;

        /// <summary>Guest-only: this client's own rack, delivered privately by the host. Never synced.</summary>
        public readonly List<char> myRack = new List<char>();

        private CoherenceSync sync;
        private PvpGame game; // non-null only on the host
        private ClientID guestClientId;
        private bool guestKnown;

        private void Awake()
        {
            sync = GetComponent<CoherenceSync>();
        }

        private void OnEnable()
        {
            CoherenceBridge bridge = sync != null ? sync.CoherenceBridge : null;
            if (bridge != null) bridge.ClientConnections.OnCreated += OnClientConnected;
        }

        private void OnDisable()
        {
            CoherenceBridge bridge = sync != null ? sync.CoherenceBridge : null;
            if (bridge != null) bridge.ClientConnections.OnCreated -= OnClientConnected;
        }

        private void OnClientConnected(CoherenceClientConnection conn)
        {
            if (conn.IsMyConnection || !sync.HasStateAuthority || guestKnown) return;
            guestKnown = true;
            guestClientId = conn.ClientId;
            sync.TransferAuthority(guestClientId, AuthorityType.Input);
        }

        public bool IsHost { get { return sync.HasStateAuthority; } }
        public bool GuestConnected { get { return guestKnown; } }

        /// <summary>Host only: begins the authoritative match once a guest is present.</summary>
        public void HostStartMatch(Dict dict, long seed)
        {
            game = new PvpGame(dict, seed);
            matchStarted = true;
            PushPublicState();
            SendGuestRack();
        }

        // ---- Host-side actions for the host's own (side A) turns ----

        public Board.Validation HostSubmit(bool isA, Move m)
        {
            Board.Validation v = game.TrySubmit(isA, m);
            if (v.ok)
            {
                PushPublicState();
                if (!isA) SendGuestRack();
            }
            else
            {
                lastError = v.error;
                errorSeq++;
            }
            return v;
        }

        public void HostPass(bool isA)
        {
            game.Pass(isA);
            PushPublicState();
        }

        public bool HostExchange(bool isA, int[] indices)
        {
            bool ok = game.Exchange(isA, indices);
            if (ok)
            {
                PushPublicState();
                if (!isA) SendGuestRack();
            }
            return ok;
        }

        /// <summary>Host only: the live board/racks/bag, for the host's own UI to read directly.</summary>
        public PvpGame HostGame { get { return game; } }

        private void PushPublicState()
        {
            PackBoard(game.board, out packedLetters, out packedBlanks);
            scoreA = game.scoreA;
            scoreB = game.scoreB;
            turnA = game.turnA;
            over = game.over;
            bagCount = game.BagSize();
            lastEvent = game.lastEvent;
        }

        private void SendGuestRack()
        {
            string tiles = new string(game.RackOf(false).ToArray());
            sync.SendCommand<string>(CmdReceiveRack, MessageTarget.InputAuthorityOnly, tiles);
        }

        // ---- Guest-side helpers: send a request to the host and wait for the ----
        // ---- next state sync (turn flips to the host = accepted; errorSeq   ----
        // ---- bumps = rejected). Keeps Coherence APIs out of the UI layer.   ----

        // Coherence commands only accept scalar parameters (int/char/bool/string/...),
        // never arrays, so variable-length payloads are packed into a single string.

        public void GuestSubmitMove(Move m)
        {
            sync.SendCommand<string>(CmdSubmitMove, MessageTarget.StateAuthorityOnly, EncodeMove(m));
        }

        public void GuestPass()
        {
            sync.SendCommand(CmdPass, MessageTarget.StateAuthorityOnly);
        }

        public void GuestExchange(int[] indices)
        {
            sync.SendCommand<string>(CmdExchange, MessageTarget.StateAuthorityOnly, EncodeInts(indices));
        }

        // ---- Commands the guest sends to the host (routed to State Authority) ----

        [Command]
        public void CmdSubmitMove(string data)
        {
            HostSubmit(false, DecodeMove(data));
        }

        [Command]
        public void CmdPass()
        {
            HostPass(false);
        }

        [Command]
        public void CmdExchange(string data)
        {
            HostExchange(false, DecodeInts(data));
        }

        // ---- Command the host sends to the guest to deliver its private rack ----

        [Command]
        public void CmdReceiveRack(string tiles)
        {
            myRack.Clear();
            myRack.AddRange(tiles.ToCharArray());
        }

        // ---- Compact string (de)serialization for command payloads ----

        private static string EncodeMove(Move m)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < m.pr.Length; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(m.pr[i]).Append(',').Append(m.pc[i]).Append(',')
                  .Append(m.pl[i]).Append(',').Append(m.pb[i] ? '1' : '0');
            }
            return sb.ToString();
        }

        private static Move DecodeMove(string data)
        {
            Move m = new Move();
            string[] parts = string.IsNullOrEmpty(data) ? new string[0] : data.Split(';');
            int n = parts.Length;
            m.pr = new int[n]; m.pc = new int[n]; m.pl = new char[n]; m.pb = new bool[n];
            for (int i = 0; i < n; i++)
            {
                string[] f = parts[i].Split(',');
                m.pr[i] = int.Parse(f[0]);
                m.pc[i] = int.Parse(f[1]);
                m.pl[i] = f[2][0];
                m.pb[i] = f[3] == "1";
            }
            return m;
        }

        private static string EncodeInts(int[] a)
        {
            return a == null ? "" : string.Join(",", a);
        }

        private static int[] DecodeInts(string s)
        {
            if (string.IsNullOrEmpty(s)) return new int[0];
            string[] parts = s.Split(',');
            int[] a = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) a[i] = int.Parse(parts[i]);
            return a;
        }

        // ---- Board (de)serialization, mirroring Game.Save()'s grid format ----

        private static void PackBoard(Board board, out string letters, out string blanks)
        {
            StringBuilder sbL = new StringBuilder();
            StringBuilder sbB = new StringBuilder();
            for (int r = 0; r < Board.N; r++)
            {
                for (int c = 0; c < Board.N; c++)
                {
                    char ch = board.LetterAt(r, c);
                    sbL.Append(ch == (char)0 ? '.' : ch);
                    sbB.Append(board.IsBlankAt(r, c) ? '1' : '0');
                }
            }
            letters = sbL.ToString();
            blanks = sbB.ToString();
        }

        /// <summary>Guest-side: rebuilds a local read-only Board view from the synced strings.</summary>
        public static void UnpackBoard(string letters, string blanks, Board board)
        {
            board.Clear();
            if (string.IsNullOrEmpty(letters)) return;
            for (int i = 0; i < Board.N * Board.N && i < letters.Length; i++)
            {
                char ch = letters[i];
                if (ch != '.') board.SetCell(i / Board.N, i % Board.N, ch, blanks[i] == '1');
            }
        }
    }
}
