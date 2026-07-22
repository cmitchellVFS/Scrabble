using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Shaderpark.Scrabble;

/// <summary>
/// Self-contained IMGUI front-end for the Scrabble engine. Bootstraps itself
/// (no scene wiring required), renders the whole game with OnGUI, runs the AI on
/// a background thread, animates AI tiles dropping in, shows word definitions,
/// and saves/resumes a single in-progress game.
/// </summary>
public class ScrabbleApp : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindObjectOfType<ScrabbleApp>() != null) return;
        GameObject go = new GameObject("ScrabbleApp");
        go.AddComponent<ScrabbleApp>();
        DontDestroyOnLoad(go);
    }

    private enum ScreenState { Loading, Start, MpLobby, Playing }

    // Colours (ARGB hex).
    private static readonly Color BG = Hex(0xFF0E1623);
    private static readonly Color CARD = Hex(0xFF16223A);
    private static readonly Color TEXT = Hex(0xFFE6ECF7);
    private static readonly Color MUTED = Hex(0xFF8FA0BF);
    private static readonly Color ACCENT = Hex(0xFF43B581);
    private static readonly Color TILE = Hex(0xFFE8D6A6);
    private static readonly Color TILE_BLANK = Hex(0xFFD8C79A);
    private static readonly Color TILE_TEXT = Hex(0xFF20140A);

    private ScreenState screen = ScreenState.Loading;
    private string loadStatus = "Loading…";

    private Dict dict;
    private Dictionary<string, string> defs;

    private Game game;
    private int elo = 1000;
    private string eloInput = "1000";

    // Provisional placement state.
    private char[,] provChar;
    private bool[,] provBlank;
    private List<int[]> provList;      // {r, c, rackIndex}
    private HashSet<int> usedIdx;
    private int selectedRack = -1;
    private bool exchangeMode;
    private HashSet<int> exchangeSel;
    private bool arrangeMode;
    private int arrangePick = -1;
    private bool busy;
    private string toast = "";
    private float toastUntil;

    // Blank chooser.
    private bool showBlank;
    private int blankR, blankC, blankRackIdx;

    // Definition overlay.
    private bool showDef;
    private string defText = "";

    // Played-words log overlay. Single-player history is authoritative in
    // Game.plays (saved with the game and restored on resume); multiplayer has
    // no persisted Game, so it is reconstructed from the synced last-event line
    // into mpPlayHistory. ActivePlayHistory() picks the right one.
    private bool showHistory;
    private readonly List<string> mpPlayHistory = new List<string>();
    private static readonly List<string> emptyHistory = new List<string>();
    private string lastCapturedEvent = "";
    private Vector2 historyScroll;

    // Quit confirmation (PC only, triggered by Esc).
    private bool showQuitConfirm;

    // Multiplayer (Coherence). "Host" runs the authoritative PvpGame locally;
    // "Guest" only renders synced state and its own privately-delivered rack.
    private bool multiplayer;
    private NetworkSession netSession;
    private ScrabbleNetworkMatch net;
    private string roomCodeInput = "";
    private readonly Board netGuestBoard = new Board();
    private bool netAwaitingResult;
    private int netLastErrorSeq;

    // Background load.
    private volatile bool loadDone;
    private Dict loadedDict;
    private Dictionary<string, string> loadedDefs;

    // AI turn + animation.
    private volatile bool aiReady;
    private Move aiPendingMove;
    private bool animating;
    private float animStart;
    private float animTotal;
    private List<float[]> animTiles;   // {r, c, letterCode, blank?1:0, delay}

    private const string SAVE_FILE = "savegame.dat";

    // Virtual portrait canvas the whole UI is laid out against. Scaled+letterboxed
    // to fit the real window so the layout (hand-tuned for a phone's portrait
    // aspect ratio) always fits on screen, including wide PC/desktop windows.
    private const float VW = 1080f;
    private const float VH = 2340f;

    private void Awake()
    {
        Application.targetFrameRate = 60;
        Screen.orientation = ScreenOrientation.Portrait;
        Camera cam = gameObject.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = BG;
        cam.cullingMask = 0; // nothing to render; OnGUI draws everything
    }

    private void Start()
    {
        StartCoroutine(LoadAssets());
    }

    // ------------------------------------------------------------------
    // Asset loading
    // ------------------------------------------------------------------

    private IEnumerator LoadAssets()
    {
        loadStatus = "Loading word list…";
        string wordsText = null, defsText = null;
        yield return DownloadText("words.txt", (s) => wordsText = s);
        loadStatus = "Loading definitions…";
        yield return DownloadText("defs.tsv", (s) => defsText = s);

        if (wordsText == null)
        {
            loadStatus = "Failed to load word list.";
            yield break;
        }

        loadStatus = "Building dictionary…";
        loadDone = false;
        string wt = wordsText, dt = defsText;
        System.Threading.Thread t = new System.Threading.Thread(() =>
        {
            loadedDict = Dict.Build(wt.Split('\n'));
            loadedDefs = ParseDefs(dt);
            loadDone = true; // volatile write publishes the two fields above
        });
        t.IsBackground = true;
        t.Start();
        while (!loadDone) yield return null;

        dict = loadedDict;
        defs = loadedDefs;
        screen = ScreenState.Start;
    }

    private IEnumerator DownloadText(string name, System.Action<string> onDone)
    {
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, name);
        string url = path.Contains("://") ? path : "file://" + path;
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();
#if UNITY_2020_1_OR_NEWER
            bool ok = req.result == UnityWebRequest.Result.Success;
#else
            bool ok = !req.isNetworkError && !req.isHttpError;
#endif
            onDone(ok ? req.downloadHandler.text : null);
        }
    }

    private static Dictionary<string, string> ParseDefs(string text)
    {
        Dictionary<string, string> d = new Dictionary<string, string>();
        if (text == null) return d;
        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int t = line.IndexOf('\t');
            if (t > 0)
            {
                string key = line.Substring(0, t);
                if (!d.ContainsKey(key)) d[key] = line.Substring(t + 1);
            }
        }
        return d;
    }

    // ------------------------------------------------------------------
    // Update: AI turn driving + animation
    // ------------------------------------------------------------------

    private void Update()
    {
        if (toast.Length > 0 && Time.unscaledTime > toastUntil) toast = "";

#if UNITY_STANDALONE || UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (showQuitConfirm) showQuitConfirm = false;
            else if (showHistory) showHistory = false;
            else if (!showBlank && !showDef) showQuitConfirm = true;
        }
#endif

        if (multiplayer) UpdateMultiplayer();
        CaptureHistory();

        if (aiReady && !animating)
        {
            aiReady = false;
            Move m = aiPendingMove;
            if (m != null && m.type == Move.NORMAL && m.TilesPlaced() > 0)
            {
                BeginAiAnimation(m);
            }
            else
            {
                CommitAi(m);
            }
        }

        if (animating && Time.time - animStart >= animTotal)
        {
            animating = false;
            CommitAi(aiPendingMove);
        }
    }

    private void UpdateMultiplayer()
    {
        if (net == null && netSession != null) net = netSession.Match;
        if (net == null) return;

        if (net.IsHost && !net.matchStarted && net.GuestConnected)
        {
            net.HostStartMatch(dict, System.DateTime.Now.Ticks);
        }

        if (net.matchStarted && screen == ScreenState.MpLobby)
        {
            InitUiState();
            screen = ScreenState.Playing;
        }

        if (netAwaitingResult)
        {
            if (net.errorSeq != netLastErrorSeq)
            {
                netLastErrorSeq = net.errorSeq;
                netAwaitingResult = false;
                Toast(net.lastError);
            }
            else if (!ActiveMyTurn() || ActiveOver())
            {
                netAwaitingResult = false;
                RecallAll();
            }
        }

        busy = !ActiveMyTurn() || netAwaitingResult;
    }

    // Single-player history is recorded authoritatively in Game.plays; only the
    // multiplayer log needs rebuilding from the synced last-event line. Passes
    // and exchanges never contain "played", so they are skipped.
    private void CaptureHistory()
    {
        if (!multiplayer || screen != ScreenState.Playing || net == null) return;
        string ev = ActiveLastEvent();
        if (ev == lastCapturedEvent) return;
        lastCapturedEvent = ev;
        if (!string.IsNullOrEmpty(ev) && ev.Contains("played")) mpPlayHistory.Add(ev);
    }

    private void BeginAiAnimation(Move m)
    {
        animTiles = new List<float[]>();
        for (int i = 0; i < m.pr.Length; i++)
        {
            animTiles.Add(new float[] { m.pr[i], m.pc[i], m.pl[i], m.pb[i] ? 1 : 0, i * 0.11f });
        }
        animStart = Time.time;
        animTotal = (m.pr.Length - 1) * 0.11f + 0.42f;
        animating = true;
    }

    private void CommitAi(Move m)
    {
        game.CommitAiMove(m);
        busy = false;
        if (!game.over && !game.humanTurn) game.humanTurn = true;
        SaveGame();
    }

    private void StartAiTurn()
    {
        busy = true;
        SaveGame(); // persist the human's committed move
        aiReady = false;
        System.Threading.Thread t = new System.Threading.Thread(() =>
        {
            Move m = game.ChooseAiMove();
            aiPendingMove = m;
            aiReady = true;
        });
        t.IsBackground = true;
        t.Start();
    }

    private void EndHumanTurn()
    {
        if (game.over) { SaveGame(); return; } // SaveGame deletes the file when over
        if (!game.humanTurn) StartAiTurn();
    }

    // ------------------------------------------------------------------
    // Multiplayer state accessors -- the shared gameplay UI/logic below reads
    // through these instead of touching `game` or `net` directly, so the same
    // Draw/handler code works for both single-player and multiplayer.
    // ------------------------------------------------------------------

    private Board ActiveBoard()
    {
        if (!multiplayer) return game.board;
        if (net.IsHost) return net.HostGame.board;
        ScrabbleNetworkMatch.UnpackBoard(net.packedLetters, net.packedBlanks, netGuestBoard);
        return netGuestBoard;
    }

    private List<char> ActiveRack()
    {
        if (!multiplayer) return game.humanRack;
        return net.IsHost ? net.HostGame.RackOf(true) : net.myRack;
    }

    private bool ActiveMyTurn()
    {
        if (!multiplayer) return game.humanTurn;
        return net.IsHost ? net.turnA : !net.turnA;
    }

    private bool ActiveOver()
    {
        return multiplayer ? net.over : game.over;
    }

    private int ActiveMyScore()
    {
        if (!multiplayer) return game.humanScore;
        return net.IsHost ? net.scoreA : net.scoreB;
    }

    private int ActiveOppScore()
    {
        if (!multiplayer) return game.aiScore;
        return net.IsHost ? net.scoreB : net.scoreA;
    }

    private int ActiveBagSize()
    {
        return multiplayer ? net.bagCount : game.BagSize();
    }

    private string ActiveLastEvent()
    {
        return multiplayer ? net.lastEvent : game.lastEvent;
    }

    private List<string> ActivePlayHistory()
    {
        if (multiplayer) return mpPlayHistory;
        return game != null ? game.plays : emptyHistory;
    }

    private string ActiveTurnLabel()
    {
        if (ActiveOver()) return "Game over";
        if (!multiplayer) return busy ? "AI thinking…" : "Your turn";
        if (netAwaitingResult) return "Sending…";
        return ActiveMyTurn() ? "Your turn" : "Opponent's turn";
    }

    // ------------------------------------------------------------------
    // Multiplayer lobby: create/join a room by a short shareable code.
    // ------------------------------------------------------------------

    private void EnsureNetSession()
    {
        if (netSession != null) return;
        GameObject go = new GameObject("NetworkSession");
        netSession = go.AddComponent<NetworkSession>();
        multiplayer = true;
    }

    private void StartHosting()
    {
        EnsureNetSession();
        netSession.HostRoom(roomCodeInput.Trim().ToUpperInvariant());
    }

    private void StartJoining()
    {
        EnsureNetSession();
        netSession.JoinRoomByCode(roomCodeInput.Trim().ToUpperInvariant());
    }

    private void LeaveMultiplayer()
    {
        if (netSession != null)
        {
            netSession.Disconnect();
            Destroy(netSession.gameObject);
        }
        netSession = null;
        net = null;
        multiplayer = false;
        netAwaitingResult = false;
        netLastErrorSeq = 0;
        roomCodeInput = "";
        screen = ScreenState.Start;
    }

    // ------------------------------------------------------------------
    // GUI
    // ------------------------------------------------------------------

    private void OnGUI()
    {
        // Fill the whole real window first (this becomes the letterbox/pillarbox
        // colour for windows that aren't VW:VH shaped, e.g. PC/desktop).
        DrawRect(new Rect(0, 0, Screen.width, Screen.height), BG);

        float scale = Mathf.Min(Screen.width / VW, Screen.height / VH);
        float offX = (Screen.width - VW * scale) / 2f;
        float offY = (Screen.height - VH * scale) / 2f;
        Matrix4x4 oldMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(new Vector3(offX, offY, 0), Quaternion.identity, new Vector3(scale, scale, 1f));

        switch (screen)
        {
            case ScreenState.Loading: DrawLoading(); break;
            case ScreenState.Start: DrawStart(); break;
            case ScreenState.MpLobby: DrawMpLobby(); break;
            case ScreenState.Playing: DrawGame(); break;
        }
        if (showBlank) DrawBlankChooser();
        if (showDef) DrawDefinition();
        if (showHistory) DrawHistory();
        bool isGameOver = multiplayer ? (net != null && net.over) : (game != null && game.over);
        if (screen == ScreenState.Playing && isGameOver) DrawGameOver();
        if (showQuitConfirm) DrawQuitConfirm();
        if (toast.Length > 0) DrawToast();

        GUI.matrix = oldMatrix;
    }

    private float U { get { return VW / 100f; } } // 1% width unit (of the virtual canvas)

    // True while a modal overlay is up. IMGUI dispatches clicks in draw/call
    // order, not visual z-order, so board/rack/control buttons underneath an
    // overlay must be disabled or they silently eat clicks meant for it.
    private bool ModalActive { get { return showBlank || showDef || showQuitConfirm || showHistory; } }

    private GUIStyle Label(int px, Color col, TextAnchor anchor, bool bold)
    {
        GUIStyle s = new GUIStyle(GUI.skin.label);
        s.fontSize = px;
        s.normal.textColor = col;
        s.alignment = anchor;
        s.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        s.wordWrap = true;
        return s;
    }

    private GUIStyle Btn(int px)
    {
        GUIStyle s = new GUIStyle(GUI.skin.button);
        s.fontSize = px;
        return s;
    }

    private void DrawLoading()
    {
        GUI.Label(new Rect(0, VH / 2 - 4 * U, VW, 8 * U),
            loadStatus, Label((int)(4 * U), TEXT, TextAnchor.MiddleCenter, false));
    }

    private void DrawStart()
    {
        float w = VW;
        float y = 8 * U;
        GUI.Label(new Rect(0, y, w, 12 * U), "Scrabble", Label((int)(9 * U), TEXT, TextAnchor.MiddleCenter, true));
        y += 13 * U;
        GUI.Label(new Rect(0, y, w, 6 * U), "Play against an AI matched to your rating",
            Label((int)(3.4f * U), MUTED, TextAnchor.MiddleCenter, false));
        y += 10 * U;
        GUI.Label(new Rect(10 * U, y, w - 20 * U, 5 * U), "Your ELO rating",
            Label((int)(3.6f * U), ACCENT, TextAnchor.MiddleCenter, false));
        y += 6 * U;
        GUIStyle field = new GUIStyle(GUI.skin.textField);
        field.fontSize = (int)(5 * U);
        field.alignment = TextAnchor.MiddleCenter;
        eloInput = GUI.TextField(new Rect(w / 2 - 20 * U, y, 40 * U, 9 * U), eloInput, field);
        y += 11 * U;
        GUI.Label(new Rect(0, y, w, 5 * U), "300 beginner · 1200 club · 2000 expert",
            Label((int)(3 * U), MUTED, TextAnchor.MiddleCenter, false));
        y += 9 * U;
        if (GUI.Button(new Rect(w / 2 - 25 * U, y, 50 * U, 10 * U), "Start Game", Btn((int)(4.2f * U))))
        {
            int e;
            if (!int.TryParse(eloInput.Trim(), out e)) e = 1000;
            elo = Mathf.Clamp(e, 100, 3000);
            BeginGame();
        }
        y += 12 * U;
        if (HasSave())
        {
            if (GUI.Button(new Rect(w / 2 - 25 * U, y, 50 * U, 10 * U), "Resume Saved Game", Btn((int)(4.2f * U))))
            {
                DoResume();
            }
            y += 12 * U;
        }
        if (GUI.Button(new Rect(w / 2 - 25 * U, y, 50 * U, 10 * U), "Play Online", Btn((int)(4.2f * U))))
        {
            screen = ScreenState.MpLobby;
        }
        y += 12 * U;
        GUI.Label(new Rect(8 * U, y, w - 16 * U, VH - y),
            "How to play\n\n" +
            "Tap a rack tile, then tap an empty board square to place it. Tap a placed " +
            "tile to take it back. Press Submit to play the word.\n\n" +
            "First word must cross the centre. Later words must connect. Blanks score 0. " +
            "All 7 tiles = +50.\n\n" +
            "Arrange reorders your rack. Tap a word on the board to see its meaning.",
            Label((int)(3 * U), MUTED, TextAnchor.UpperLeft, false));
    }

    private void DrawMpLobby()
    {
        float w = VW;
        float y = 10 * U;
        GUI.Label(new Rect(0, y, w, 10 * U), "Play Online", Label((int)(6 * U), TEXT, TextAnchor.MiddleCenter, true));
        y += 14 * U;

        bool connecting = netSession != null && netSession.IsConnecting;

        if (net == null)
        {
            GUI.Label(new Rect(10 * U, y, w - 20 * U, 5 * U), "Room code",
                Label((int)(3.6f * U), ACCENT, TextAnchor.MiddleCenter, false));
            y += 6 * U;
            GUIStyle field = new GUIStyle(GUI.skin.textField);
            field.fontSize = (int)(5 * U);
            field.alignment = TextAnchor.MiddleCenter;
            string typed = GUI.TextField(new Rect(w / 2 - 20 * U, y, 40 * U, 9 * U), roomCodeInput, field);
            if (typed.Length <= 6) roomCodeInput = typed.ToUpperInvariant();
            y += 13 * U;

            GUI.enabled = !connecting && roomCodeInput.Trim().Length > 0;
            if (GUI.Button(new Rect(w / 2 - 25 * U, y, 50 * U, 10 * U), "Host New Room", Btn((int)(4.2f * U))))
            {
                StartHosting();
            }
            y += 12 * U;
            if (GUI.Button(new Rect(w / 2 - 25 * U, y, 50 * U, 10 * U), "Join Room", Btn((int)(4.2f * U))))
            {
                StartJoining();
            }
            y += 12 * U;
            GUI.enabled = true;
        }

        if (netSession != null && netSession.ErrorText != null)
        {
            GUI.Label(new Rect(6 * U, y, w - 12 * U, 10 * U), netSession.ErrorText,
                Label((int)(3.4f * U), Hex(0xFFE05555), TextAnchor.MiddleCenter, false));
            y += 12 * U;
        }
        else if (connecting)
        {
            GUI.Label(new Rect(6 * U, y, w - 12 * U, 10 * U), netSession.StatusText,
                Label((int)(3.4f * U), MUTED, TextAnchor.MiddleCenter, false));
            y += 12 * U;
        }
        else if (net != null && !net.matchStarted)
        {
            GUI.Label(new Rect(6 * U, y, w - 12 * U, 10 * U),
                net.IsHost ? "Waiting for opponent to join..." : "Waiting for host to start...",
                Label((int)(3.4f * U), MUTED, TextAnchor.MiddleCenter, false));
            y += 12 * U;
        }

        if (GUI.Button(new Rect(w / 2 - 25 * U, VH - 14 * U, 50 * U, 10 * U), "Back", Btn((int)(3.8f * U))))
        {
            LeaveMultiplayer();
        }
    }

    private void DrawGame()
    {
        float w = VW;
        float headerH = 11 * U;
        GUI.Label(new Rect(2 * U, 0, w - 4 * U, 6 * U),
            "You " + ActiveMyScore() + "   " + (multiplayer ? "Opp " : "AI ") + ActiveOppScore() +
            "   Bag " + ActiveBagSize() + "   " + ActiveTurnLabel(),
            Label((int)(3.6f * U), TEXT, TextAnchor.MiddleLeft, true));
        // Last scored move -- tap to open the running list of played words.
        Rect eventRect = new Rect(2 * U, 6 * U, w - 4 * U, 5 * U);
        bool hasHistory = ActivePlayHistory().Count > 0;
        GUI.Label(eventRect, hasHistory ? ActiveLastEvent() + "   ›" : ActiveLastEvent(),
            Label((int)(3 * U), ACCENT, TextAnchor.MiddleLeft, false));
        if (hasHistory && !ModalActive && GUI.Button(eventRect, GUIContent.none, GUIStyle.none))
        {
            showHistory = true;
            historyScroll = Vector2.zero;
        }

        float controlsH = 22 * U;
        float rackH = w / 8f + 2 * U;
        float boardMax = VH - headerH - controlsH - rackH - 3 * U;
        float boardSize = Mathf.Min(w - 2 * U, boardMax);
        float cell = boardSize / 15f;
        float bx = (w - boardSize) / 2f;
        float by = headerH;

        DrawBoard(bx, by, cell);

        // Rack.
        float rackTop = by + boardSize + 1.5f * U;
        DrawRack(rackTop, w);

        // Controls.
        float cy = rackTop + rackH;
        DrawControls(cy, w);
    }

    private void DrawBoard(float bx, float by, float cell)
    {
        Board board = ActiveBoard();
        // Visuals first.
        for (int r = 0; r < 15; r++)
        {
            for (int c = 0; c < 15; c++)
            {
                Rect rect = new Rect(bx + c * cell, by + r * cell, cell - 1, cell - 1);
                char committed = board.LetterAt(r, c);
                char prov = provChar[r, c];
                if (committed == (char)0 && prov == (char)0)
                {
                    char p = ScrabbleData.PremiumAt(r, c);
                    DrawRect(rect, PremiumColor(p));
                    string lab = PremiumLabel(p);
                    if (lab.Length > 0)
                        GUI.Label(rect, lab, Label((int)(cell * 0.30f), Hex(0xFFD7E0F0), TextAnchor.MiddleCenter, false));
                }
                else
                {
                    bool isProv = prov != (char)0;
                    char ch = isProv ? prov : committed;
                    bool blank = isProv ? provBlank[r, c] : board.IsBlankAt(r, c);
                    DrawTile(rect, ch, blank, isProv, cell);
                }
            }
        }
        // Animated AI tiles on top.
        if (animating && animTiles != null)
        {
            float elapsed = Time.time - animStart;
            for (int i = 0; i < animTiles.Count; i++)
            {
                float[] a = animTiles[i];
                float p = (elapsed - a[4]) / 0.40f;
                if (p < 0) p = 0; if (p > 1) p = 1;
                float eased = 1f - (1f - p) * (1f - p) * (1f - p);
                float targetY = by + a[0] * cell;
                float startY = by - cell * 1.5f;
                float y = startY + (targetY - startY) * eased;
                float x = bx + a[1] * cell;
                DrawTile(new Rect(x, y, cell - 1, cell - 1), (char)a[2], a[3] > 0.5f, true, cell);
            }
        }
        // Invisible click buttons on top.
        if (!busy && !animating && !ModalActive)
        {
            for (int r = 0; r < 15; r++)
            {
                for (int c = 0; c < 15; c++)
                {
                    Rect rect = new Rect(bx + c * cell, by + r * cell, cell, cell);
                    if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                    {
                        HandleCellTap(r, c);
                    }
                }
            }
        }
    }

    private void DrawTile(Rect rect, char ch, bool blank, bool prov, float cell)
    {
        if (prov) DrawRect(rect, ACCENT);
        Rect inner = prov ? new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height - 4) : rect;
        DrawRect(inner, blank ? TILE_BLANK : TILE);
        GUI.Label(inner, ch == '?' ? "?" : ch.ToString(),
            Label((int)(cell * 0.5f), TILE_TEXT, TextAnchor.MiddleCenter, true));
        if (!blank && ch != '?')
        {
            Rect vr = new Rect(inner.x, inner.y, inner.width - cell * 0.08f, inner.height - cell * 0.05f);
            GUI.Label(vr, ScrabbleData.Value(ch).ToString(),
                Label((int)(cell * 0.26f), Hex(0xFF4A3A20), TextAnchor.LowerRight, false));
        }
    }

    private void DrawRack(float top, float w)
    {
        List<char> rack = ActiveRack();
        int n = rack.Count;
        float size = w / 8.5f;
        float gap = size * 0.12f;
        float totalW = n * size + (n - 1) * gap;
        float x0 = (w - totalW) / 2f;
        for (int i = 0; i < n; i++)
        {
            Rect rect = new Rect(x0 + i * (size + gap), top, size, size);
            char ch = rack[i];
            bool used = usedIdx.Contains(i);
            bool sel = selectedRack == i;
            bool exch = exchangeMode && exchangeSel.Contains(i);
            bool pick = arrangeMode && arrangePick == i;

            if (used) DrawRect(rect, CARD);
            else if (pick) DrawRect(rect, Hex(0xFF4A8FBF));
            else if (exch) DrawRect(rect, Hex(0xFFE8A33A));
            else if (sel) DrawRect(rect, ACCENT);
            else DrawRect(rect, TILE);

            if (!used)
            {
                Rect inner = (sel || exch || pick) ? new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6) : rect;
                if (sel || exch || pick) DrawRect(inner, TILE);
                GUI.Label(inner, ch == '?' ? "?" : ch.ToString(),
                    Label((int)(size * 0.5f), TILE_TEXT, TextAnchor.MiddleCenter, true));
                if (ch != '?')
                    GUI.Label(new Rect(inner.x, inner.y, inner.width - size * 0.1f, inner.height - size * 0.06f),
                        ScrabbleData.Value(ch).ToString(),
                        Label((int)(size * 0.26f), Hex(0xFF4A3A20), TextAnchor.LowerRight, false));
            }
            if (!busy && !animating && !ModalActive && GUI.Button(rect, GUIContent.none, GUIStyle.none))
            {
                OnRackTap(i);
            }
        }
    }

    private void DrawControls(float y, float w)
    {
        int fs = (int)(3.3f * U);
        float bw = (w - 5 * U) / 4f;
        float bh = 9 * U;
        float x = 1 * U;
        bool can = !busy && !animating && !ModalActive && !ActiveOver() && ActiveMyTurn();
        GUI.enabled = can && !exchangeMode && !arrangeMode;
        if (GUI.Button(new Rect(x, y, bw, bh), "Submit", Btn(fs))) OnSubmit();
        if (GUI.Button(new Rect(x + bw + U, y, bw, bh), "Recall", Btn(fs))) RecallAll();
        if (GUI.Button(new Rect(x + 2 * (bw + U), y, bw, bh), "Shuffle", Btn(fs))) OnShuffle();
        GUI.enabled = can && !exchangeMode;
        if (GUI.Button(new Rect(x + 3 * (bw + U), y, bw, bh), arrangeMode ? "Done" : "Arrange", Btn(fs))) OnArrange();

        float y2 = y + bh + U;
        float bw3 = (w - 4 * U) / 3f;
        GUI.enabled = can && !exchangeMode && !arrangeMode;
        if (GUI.Button(new Rect(x, y2, bw3, bh), "Pass", Btn(fs))) OnPass();
        GUI.enabled = can && !arrangeMode;
        if (GUI.Button(new Rect(x + bw3 + U, y2, bw3, bh), exchangeMode ? "Confirm" : "Exchange", Btn(fs))) OnExchange();
        GUI.enabled = !ModalActive; // leaving is always allowed, even mid-turn
        if (GUI.Button(new Rect(x + 2 * (bw3 + U), y2, bw3, bh), "New Game", Btn(fs)))
        {
            if (multiplayer) LeaveMultiplayer();
            else screen = ScreenState.Start;
        }
        GUI.enabled = true;
    }

    // ------------------------------------------------------------------
    // Interaction
    // ------------------------------------------------------------------

    private void HandleCellTap(int r, int c)
    {
        if (exchangeMode || arrangeMode) return;
        if (provChar[r, c] == (char)0 && !ActiveBoard().IsEmptyCell(r, c))
        {
            ShowDefinition(r, c);
            return;
        }
        if (ActiveOver() || !ActiveMyTurn()) return;
        if (provChar[r, c] != (char)0) { RecallCell(r, c); return; }
        if (selectedRack < 0) { Toast("Pick a tile from your rack first"); return; }
        char ch = ActiveRack()[selectedRack];
        if (ch == '?')
        {
            showBlank = true; blankR = r; blankC = c; blankRackIdx = selectedRack;
        }
        else
        {
            PlaceTile(r, c, selectedRack, ch, false);
        }
    }

    private void OnRackTap(int idx)
    {
        if (ActiveOver() || !ActiveMyTurn()) return;
        List<char> rack = ActiveRack();
        if (arrangeMode)
        {
            if (arrangePick < 0) arrangePick = idx;
            else if (arrangePick == idx) arrangePick = -1;
            else
            {
                char t = rack[arrangePick];
                rack.RemoveAt(arrangePick);
                int insert = (arrangePick < idx) ? idx - 1 : idx;
                if (insert > rack.Count) insert = rack.Count;
                rack.Insert(insert, t);
                arrangePick = -1;
            }
            return;
        }
        if (exchangeMode)
        {
            if (exchangeSel.Contains(idx)) exchangeSel.Remove(idx);
            else if (!usedIdx.Contains(idx)) exchangeSel.Add(idx);
            return;
        }
        if (usedIdx.Contains(idx)) return;
        selectedRack = (selectedRack == idx) ? -1 : idx;
    }

    private void PlaceTile(int r, int c, int rackIndex, char letter, bool blank)
    {
        provChar[r, c] = letter;
        provBlank[r, c] = blank;
        provList.Add(new int[] { r, c, rackIndex });
        usedIdx.Add(rackIndex);
        selectedRack = -1;
    }

    private void RecallCell(int r, int c)
    {
        for (int i = 0; i < provList.Count; i++)
        {
            int[] p = provList[i];
            if (p[0] == r && p[1] == c) { usedIdx.Remove(p[2]); provList.RemoveAt(i); break; }
        }
        provChar[r, c] = (char)0;
        provBlank[r, c] = false;
    }

    private void RecallAll()
    {
        for (int i = 0; i < provList.Count; i++)
        {
            int[] p = provList[i];
            provChar[p[0], p[1]] = (char)0;
            provBlank[p[0], p[1]] = false;
        }
        provList.Clear();
        usedIdx.Clear();
        selectedRack = -1;
    }

    private void OnSubmit()
    {
        if (provList.Count == 0) { Toast("Place some tiles first"); return; }
        Move m = new Move();
        int n = provList.Count;
        m.pr = new int[n]; m.pc = new int[n]; m.pl = new char[n]; m.pb = new bool[n];
        for (int i = 0; i < n; i++)
        {
            int[] p = provList[i];
            m.pr[i] = p[0]; m.pc[i] = p[1];
            m.pl[i] = provChar[p[0], p[1]];
            m.pb[i] = provBlank[p[0], p[1]];
        }

        if (multiplayer)
        {
            if (net.IsHost)
            {
                Board.Validation hv = net.HostSubmit(true, m);
                if (!hv.ok) { Toast(hv.error); return; }
                RecallAll();
            }
            else
            {
                net.GuestSubmitMove(m);
                netAwaitingResult = true;
            }
            return;
        }

        Board.Validation v = game.TrySubmitHuman(m);
        if (!v.ok) { Toast(v.error); return; }
        RecallAll();
        EndHumanTurn();
    }

    private void OnShuffle()
    {
        if (provList.Count > 0) { Toast("Recall your tiles before shuffling"); return; }
        // Fisher-Yates on the rack.
        System.Random rr = new System.Random();
        List<char> rack = ActiveRack();
        for (int i = rack.Count - 1; i > 0; i--)
        {
            int j = rr.Next(i + 1);
            char tmp = rack[i]; rack[i] = rack[j]; rack[j] = tmp;
        }
        selectedRack = -1;
    }

    private void OnArrange()
    {
        if (!arrangeMode)
        {
            if (provList.Count > 0) { Toast("Recall your tiles before rearranging"); return; }
            arrangeMode = true; arrangePick = -1; selectedRack = -1;
            exchangeMode = false; exchangeSel.Clear();
            Toast("Tap a tile, then tap where to move it");
        }
        else { arrangeMode = false; arrangePick = -1; }
    }

    private void OnPass()
    {
        RecallAll();
        if (multiplayer)
        {
            if (net.IsHost) net.HostPass(true);
            else { net.GuestPass(); netAwaitingResult = true; }
            return;
        }
        game.PassHuman();
        EndHumanTurn();
    }

    private void OnExchange()
    {
        if (!exchangeMode)
        {
            if (provList.Count > 0) { Toast("Recall your tiles first"); return; }
            if (ActiveBagSize() == 0) { Toast("Bag is empty — cannot exchange"); return; }
            exchangeMode = true; exchangeSel.Clear(); selectedRack = -1;
            arrangeMode = false; arrangePick = -1;
            Toast("Tap tiles to swap, then press Confirm");
            return;
        }
        if (exchangeSel.Count == 0) { exchangeMode = false; return; }
        int[] idx = new int[exchangeSel.Count];
        exchangeSel.CopyTo(idx);
        exchangeMode = false; exchangeSel.Clear();

        if (multiplayer)
        {
            if (net.IsHost) net.HostExchange(true, idx);
            else { net.GuestExchange(idx); netAwaitingResult = true; }
            return;
        }
        bool ok = game.ExchangeHuman(idx);
        if (ok) EndHumanTurn();
    }

    // ------------------------------------------------------------------
    // Definitions
    // ------------------------------------------------------------------

    private string WordAt(int r, int c, int dr, int dc)
    {
        Board board = ActiveBoard();
        if (board.IsEmptyCell(r, c)) return null;
        int sr = r, sc = c;
        while (InB(sr - dr, sc - dc) && !board.IsEmptyCell(sr - dr, sc - dc)) { sr -= dr; sc -= dc; }
        int er = r, ec = c;
        while (InB(er + dr, ec + dc) && !board.IsEmptyCell(er + dr, ec + dc)) { er += dr; ec += dc; }
        int len = Mathf.Abs(er - sr) + Mathf.Abs(ec - sc) + 1;
        if (len < 2) return null;
        StringBuilder sb = new StringBuilder();
        int cr = sr, cc = sc;
        for (int i = 0; i < len; i++) { sb.Append(board.LetterAt(cr, cc)); cr += dr; cc += dc; }
        return sb.ToString();
    }

    private static bool InB(int r, int c) { return r >= 0 && r < Board.N && c >= 0 && c < Board.N; }

    private void ShowDefinition(int r, int c)
    {
        List<string> words = new List<string>();
        string h = WordAt(r, c, 0, 1);
        string v = WordAt(r, c, 1, 0);
        if (h != null) words.Add(h);
        if (v != null && v != h) words.Add(v);
        if (words.Count == 0) return;

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < words.Count; i++)
        {
            string w = words[i];
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(w).Append(":\n");
            string def = LookupDef(w);
            sb.Append(def ?? "Valid word — no definition available offline.");
        }
        defText = sb.ToString();
        showDef = true;
    }

    private string LookupDef(string w)
    {
        string d;
        if (defs.TryGetValue(w, out d)) return d;
        List<string> bases = Stemmer.Bases(w);
        for (int i = 0; i < bases.Count; i++)
        {
            if (defs.TryGetValue(bases[i], out d))
                return d + "\n(inflected form of " + bases[i] + ")";
        }
        return null;
    }

    // ------------------------------------------------------------------
    // Overlays
    // ------------------------------------------------------------------

    private void DrawBlankChooser()
    {
        float w = VW;
        Rect box = new Rect(w * 0.08f, VH * 0.25f, w * 0.84f, VH * 0.5f);
        DrawRect(box, CARD);
        GUI.Label(new Rect(box.x, box.y + 2 * U, box.width, 6 * U), "Choose a letter",
            Label((int)(4 * U), TEXT, TextAnchor.MiddleCenter, true));
        float cs = box.width / 6.5f;
        for (int i = 0; i < 26; i++)
        {
            int row = i / 6, col = i % 6;
            Rect r = new Rect(box.x + 2 * U + col * (cs + U * 0.4f), box.y + 9 * U + row * (cs + U * 0.4f), cs, cs);
            if (GUI.Button(r, ((char)('A' + i)).ToString(), Btn((int)(cs * 0.4f))))
            {
                showBlank = false;
                PlaceTile(blankR, blankC, blankRackIdx, (char)('A' + i), true);
            }
        }
    }

    private Vector2 defScroll;
    private void DrawDefinition()
    {
        float w = VW;
        Rect box = new Rect(w * 0.06f, VH * 0.25f, w * 0.88f, VH * 0.5f);
        DrawRect(box, CARD);
        Rect inner = new Rect(box.x + 3 * U, box.y + 3 * U, box.width - 6 * U, box.height - 16 * U);
        defScroll = GUI.BeginScrollView(inner, defScroll, new Rect(0, 0, inner.width - 4 * U, VH));
        GUI.Label(new Rect(0, 0, inner.width - 4 * U, VH), defText,
            Label((int)(3.4f * U), TEXT, TextAnchor.UpperLeft, false));
        GUI.EndScrollView();
        if (GUI.Button(new Rect(box.x + box.width / 2 - 15 * U, box.y + box.height - 11 * U, 30 * U, 9 * U), "Close", Btn((int)(3.6f * U))))
        {
            showDef = false; defScroll = Vector2.zero;
        }
    }

    private void DrawHistory()
    {
        float w = VW;
        Rect box = new Rect(w * 0.06f, VH * 0.18f, w * 0.88f, VH * 0.64f);
        DrawRect(box, CARD);
        GUI.Label(new Rect(box.x, box.y + 2 * U, box.width, 6 * U), "Played words",
            Label((int)(4 * U), TEXT, TextAnchor.MiddleCenter, true));

        Rect inner = new Rect(box.x + 3 * U, box.y + 9 * U, box.width - 6 * U, box.height - 22 * U);
        List<string> hist = ActivePlayHistory();
        if (hist.Count == 0)
        {
            GUI.Label(inner, "No words played yet.", Label((int)(3.4f * U), MUTED, TextAnchor.UpperLeft, false));
        }
        else
        {
            // Most recent play first.
            StringBuilder sb = new StringBuilder();
            for (int i = hist.Count - 1; i >= 0; i--)
            {
                sb.Append(hist[i]);
                if (i > 0) sb.Append('\n');
            }
            float lineH = 4.6f * U;
            float contentH = Mathf.Max(inner.height, hist.Count * lineH + 2 * U);
            historyScroll = GUI.BeginScrollView(inner, historyScroll, new Rect(0, 0, inner.width - 4 * U, contentH));
            GUI.Label(new Rect(0, 0, inner.width - 4 * U, contentH), sb.ToString(),
                Label((int)(3.4f * U), TEXT, TextAnchor.UpperLeft, false));
            GUI.EndScrollView();
        }

        if (GUI.Button(new Rect(box.x + box.width / 2 - 15 * U, box.y + box.height - 11 * U, 30 * U, 9 * U), "Close", Btn((int)(3.6f * U))))
        {
            showHistory = false; historyScroll = Vector2.zero;
        }
    }

    private void DrawGameOver()
    {
        float w = VW;
        Rect box = new Rect(w * 0.1f, VH * 0.32f, w * 0.8f, VH * 0.32f);
        DrawRect(box, CARD);
        int myScore = ActiveMyScore();
        int oppScore = ActiveOppScore();
        string result = myScore > oppScore ? "You win!" :
            (oppScore > myScore ? (multiplayer ? "Opponent wins." : "AI wins.") : "It's a tie.");
        GUI.Label(new Rect(box.x, box.y + 3 * U, box.width, 8 * U), result,
            Label((int)(5.5f * U), ACCENT, TextAnchor.MiddleCenter, true));
        GUI.Label(new Rect(box.x, box.y + 12 * U, box.width, 8 * U),
            "You " + myScore + "   ·   " + (multiplayer ? "Opp " : "AI ") + oppScore,
            Label((int)(4 * U), TEXT, TextAnchor.MiddleCenter, false));
        if (GUI.Button(new Rect(box.x + box.width / 2 - 18 * U, box.y + box.height - 11 * U, 36 * U, 9 * U), "New Game", Btn((int)(3.8f * U))))
        {
            if (multiplayer) LeaveMultiplayer();
            else { DeleteSave(); screen = ScreenState.Start; }
        }
    }

    private void DrawQuitConfirm()
    {
        float w = VW;
        Rect box = new Rect(w * 0.1f, VH * 0.4f, w * 0.8f, VH * 0.16f);
        DrawRect(box, CARD);
        GUI.Label(new Rect(box.x, box.y + 2 * U, box.width, 8 * U), "Quit the game?",
            Label((int)(4.4f * U), TEXT, TextAnchor.MiddleCenter, true));
        float bw = (box.width - 6 * U) / 2f;
        float by = box.y + box.height - 11 * U;
        if (GUI.Button(new Rect(box.x + 2 * U, by, bw, 9 * U), "Cancel", Btn((int)(3.8f * U))))
        {
            showQuitConfirm = false;
        }
        if (GUI.Button(new Rect(box.x + 4 * U + bw, by, bw, 9 * U), "Quit", Btn((int)(3.8f * U))))
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    private void DrawToast()
    {
        Rect r = new Rect(VW * 0.1f, VH * 0.86f, VW * 0.8f, 8 * U);
        DrawRect(r, Hex(0xEE223047));
        GUI.Label(r, toast, Label((int)(3.2f * U), TEXT, TextAnchor.MiddleCenter, false));
    }

    private void Toast(string msg)
    {
        toast = msg;
        toastUntil = Time.unscaledTime + 2.5f;
    }

    // ------------------------------------------------------------------
    // Game lifecycle + persistence
    // ------------------------------------------------------------------

    private void InitUiState()
    {
        provChar = new char[Board.N, Board.N];
        provBlank = new bool[Board.N, Board.N];
        provList = new List<int[]>();
        usedIdx = new HashSet<int>();
        exchangeSel = new HashSet<int>();
        selectedRack = -1;
        exchangeMode = false;
        arrangeMode = false;
        arrangePick = -1;
        busy = false;
        showDef = false;
        showBlank = false;
        showHistory = false;
        mpPlayHistory.Clear();
        lastCapturedEvent = "";
        historyScroll = Vector2.zero;
    }

    private void BeginGame()
    {
        game = new Game(dict, elo, System.DateTime.Now.Ticks);
        InitUiState();
        screen = ScreenState.Playing;
        SaveGame();
    }

    private void DoResume()
    {
        string data = ReadSave();
        Game g = (data != null) ? Game.Restore(dict, data) : null;
        if (g == null) { Toast("Saved game could not be loaded"); DeleteSave(); return; }
        game = g;
        elo = g.elo;
        InitUiState();
        screen = ScreenState.Playing;
        if (!game.over && !game.humanTurn) StartAiTurn();
    }

    private string SavePath { get { return System.IO.Path.Combine(Application.persistentDataPath, SAVE_FILE); } }

    private void SaveGame()
    {
        if (game == null) return;
        if (game.over) { DeleteSave(); return; }
        try { System.IO.File.WriteAllText(SavePath, game.Save()); }
        catch (System.Exception) { }
    }

    private void DeleteSave()
    {
        try { if (System.IO.File.Exists(SavePath)) System.IO.File.Delete(SavePath); }
        catch (System.Exception) { }
    }

    private bool HasSave()
    {
        try { return System.IO.File.Exists(SavePath) && new System.IO.FileInfo(SavePath).Length > 0; }
        catch (System.Exception) { return false; }
    }

    private string ReadSave()
    {
        try { return System.IO.File.ReadAllText(SavePath); }
        catch (System.Exception) { return null; }
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused) SaveGame();
    }

    private void OnApplicationQuit()
    {
        SaveGame();
    }

    // ------------------------------------------------------------------
    // Draw helpers
    // ------------------------------------------------------------------

    private static void DrawRect(Rect r, Color c)
    {
        Color old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    private static Color PremiumColor(char p)
    {
        switch (p)
        {
            case 'T': return Hex(0xFFB23A48);
            case 'D': return Hex(0xFFC77DA0);
            case '*': return Hex(0xFFC77DA0);
            case 't': return Hex(0xFF2E5E8C);
            case 'd': return Hex(0xFF4A8FBF);
            default: return Hex(0xFF1B2A44);
        }
    }

    private static string PremiumLabel(char p)
    {
        switch (p)
        {
            case 'T': return "TW";
            case 'D': return "DW";
            case '*': return "★";
            case 't': return "TL";
            case 'd': return "DL";
            default: return "";
        }
    }

    private static Color Hex(uint argb)
    {
        float a = ((argb >> 24) & 0xFF) / 255f;
        float r = ((argb >> 16) & 0xFF) / 255f;
        float g = ((argb >> 8) & 0xFF) / 255f;
        float b = (argb & 0xFF) / 255f;
        return new Color(r, g, b, a);
    }
}
