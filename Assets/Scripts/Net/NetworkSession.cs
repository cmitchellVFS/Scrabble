using System;
using System.Threading.Tasks;
using Coherence.Cloud;
using Coherence.Toolkit;
using UnityEngine;

namespace Shaderpark.Scrabble
{
    /// <summary>
    /// Handles the Coherence Cloud plumbing for a single two-player room: guest
    /// login, create-or-join by a short shareable room code, and locating the
    /// networked <see cref="ScrabbleNetworkMatch"/> once connected. Self-contained,
    /// like the rest of this app -- no scene wiring required.
    /// </summary>
    public class NetworkSession : MonoBehaviour
    {
        private const string MatchPrefabResourceName = "ScrabbleNetworkMatch";

        public bool IsConnecting { get; private set; }
        public string StatusText { get; private set; } = "";
        public string ErrorText { get; private set; }
        public ScrabbleNetworkMatch Match { get; private set; }
        public bool IsHost { get; private set; }

        private CoherenceBridge bridge;

        private CoherenceBridge EnsureBridge()
        {
            if (bridge != null) return bridge;
            if (!CoherenceBridgeStore.TryGetBridge(gameObject.scene, out bridge))
            {
                GameObject go = new GameObject("CoherenceBridge");
                bridge = go.AddComponent<CoherenceBridge>();
                // Without a live query, a client expresses interest in no entities and
                // therefore never receives any -- the guest would connect but never see
                // the host's ScrabbleNetworkMatch. This is a board game, not a spatial
                // one, so use an unbounded query (Extent 0 = all entities regardless of
                // position) to take in the whole room.
                CoherenceLiveQuery query = go.AddComponent<CoherenceLiveQuery>();
                query.Extent = 0f;
            }
            return bridge;
        }

        public void Disconnect()
        {
            if (Match != null)
            {
                Destroy(Match.gameObject);
                Match = null;
            }
            if (bridge != null)
            {
                bridge.Disconnect();
                Destroy(bridge.gameObject);
                bridge = null;
            }
        }

        public async void HostRoom(string roomCode)
        {
            IsHost = true;
            await ConnectAndPrepare(roomCode, true);
        }

        public async void JoinRoomByCode(string roomCode)
        {
            IsHost = false;
            await ConnectAndPrepare(roomCode, false);
        }

        private async Task ConnectAndPrepare(string roomCode, bool isHost)
        {
            IsConnecting = true;
            ErrorText = null;
            try
            {
                StatusText = "Signing in...";
                await CoherenceCloud.LoginAsGuest();
                PlayerAccount account = await PlayerAccount.GetMainAsync();

                StatusText = "Finding region...";
                Region[] regions = await account.Services.Regions.FetchRegionsAsync();
                if (regions.Length == 0)
                {
                    ErrorText = "No Coherence regions available.";
                    return;
                }
                CloudRoomsService roomsService = account.Services.Rooms.GetRoomServiceForRegion(regions[0]);

                RoomData room;
                if (isHost)
                {
                    StatusText = "Creating room " + roomCode + "...";
                    RoomCreationOptions options = new RoomCreationOptions();
                    // Invite code lives as a tag so the guest can find this exact
                    // room via FetchRoomsAsync (matchmaking is tag-filtered). Also
                    // mirrored into KV for readability. Cap at 2 -- this is 1v1.
                    options.Tags = new[] { roomCode };
                    options.KeyValues["code"] = roomCode;
                    options.Name = "Scrabble " + roomCode;
                    options.MaxClients = 2;
                    room = await roomsService.CreateRoomAsync(options);
                }
                else
                {
                    StatusText = "Looking for room " + roomCode + "...";
                    System.Collections.Generic.IReadOnlyList<RoomData> rooms =
                        await roomsService.FetchRoomsAsync(new[] { roomCode });
                    if (rooms.Count == 0)
                    {
                        ErrorText = "No room found with that code.";
                        return;
                    }
                    room = rooms[0];
                }

                CoherenceBridge b = EnsureBridge();
                StatusText = "Connecting...";
                b.JoinRoom(room);

                float deadline = Time.unscaledTime + 15f;
                while (!b.IsConnected && Time.unscaledTime < deadline) await Task.Yield();
                if (!b.IsConnected)
                {
                    ErrorText = "Failed to connect to room.";
                    return;
                }

                if (isHost)
                {
                    GameObject prefab = Resources.Load<GameObject>(MatchPrefabResourceName);
                    if (prefab == null)
                    {
                        ErrorText = "Missing Resources/" + MatchPrefabResourceName + ".prefab -- " +
                            "open the project in the Unity Editor once to auto-generate it.";
                        return;
                    }
                    GameObject go = Instantiate(prefab);
                    Match = go.GetComponent<ScrabbleNetworkMatch>();
                    StatusText = "Waiting for opponent...";
                }
                else
                {
                    StatusText = "Waiting for host...";
                    deadline = Time.unscaledTime + 15f;
                    while (Match == null && Time.unscaledTime < deadline)
                    {
                        Match = FindAnyObjectByType<ScrabbleNetworkMatch>();
                        if (Match == null) await Task.Yield();
                    }
                    if (Match == null)
                    {
                        ErrorText = "Could not find the host's match.";
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorText = ex.Message;
            }
            finally
            {
                IsConnecting = false;
            }
        }
    }
}
