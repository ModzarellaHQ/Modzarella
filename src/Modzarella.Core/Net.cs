using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx.Configuration;
using Steamworks;
using UnityEngine;

namespace Modz
{
    // Mod-to-mod messages in online lobbies, on a Steam channel the game itself doesn't use.
    // Mods only run online when every player in the lobby has the same mods and opted in.
    public class Net : MonoBehaviour
    {
        const int Channel = 77;
        const string Key = "modzarella";

        public static bool Ready { get; private set; }
        internal static ConfigEntry<bool> Enabled;
        static Callback<SteamNetworkingMessagesSessionRequest_t> sessionRequest;
        static readonly IntPtr[] inbox = new IntPtr[64];
        string advertised;
        float nextCheck;

        public void Init(ConfigFile config)
        {
            Enabled = config.Bind("Online", "Online mods (experimental)", false,
                "Run mods in online lobbies where every player has Modzarella with the same mods. Others see what your mods do.");
        }

        static CSteamID Lobby()
        {
            try
            {
                if (!LobbyManager.Instance || !GameManager.Instance || GameManager.Instance.playingOffline) return CSteamID.Nil;
                return LobbyManager.Instance.GetLobbyID();
            }
            catch { return CSteamID.Nil; }
        }

        static IEnumerable<CSteamID> Members(CSteamID lobby)
        {
            int n = SteamMatchmaking.GetNumLobbyMembers(lobby);
            for (int i = 0; i < n; i++) yield return SteamMatchmaking.GetLobbyMemberByIndex(lobby, i);
        }

        static string Signature() =>
            string.Join(",", LuaEngine.Mods.Where(m => m.Error == null).Select(m => m.Id + "@" + m.Version).OrderBy(s => s, StringComparer.Ordinal));

        void Update()
        {
            if (Time.unscaledTime < nextCheck) { if (Ready) Receive(); return; }
            nextCheck = Time.unscaledTime + 1f;
            var lobby = Lobby();
            if (lobby == CSteamID.Nil || !SteamManager.Initialized) { Ready = false; advertised = null; return; }

            string mine = Enabled.Value ? Signature() : "";
            if (mine != advertised) { SteamMatchmaking.SetLobbyMemberData(lobby, Key, mine); advertised = mine; }
            sessionRequest ??= Callback<SteamNetworkingMessagesSessionRequest_t>.Create(OnSessionRequest);

            bool ready = mine.Length > 0 && Members(lobby).All(m => SteamMatchmaking.GetLobbyMemberData(lobby, m, Key) == mine);
            if (ready != Ready) ModCommon.Toast(ready ? "Online mods on: everyone here has the same mods" : "Online mods off");
            Ready = ready;
            if (Ready) Receive();
        }

        static void OnSessionRequest(SteamNetworkingMessagesSessionRequest_t req)
        {
            var lobby = Lobby();
            var who = req.m_identityRemote.GetSteamID();
            if (lobby != CSteamID.Nil && Members(lobby).Contains(who)) SteamNetworkingMessages.AcceptSessionWithUser(ref req.m_identityRemote);
        }

        public static void Send(string name, string payload, bool reliable)
        {
            var lobby = Lobby();
            if (!Ready || lobby == CSteamID.Nil) return;
            var bytes = Encoding.UTF8.GetBytes(name + "\n" + payload);
            if (bytes.Length > 1100 && !reliable) return;
            var me = SteamUser.GetSteamID();
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                foreach (var m in Members(lobby))
                {
                    if (m == me) continue;
                    var id = new SteamNetworkingIdentity();
                    id.SetSteamID(m);
                    SteamNetworkingMessages.SendMessageToUser(ref id, handle.AddrOfPinnedObject(), (uint)bytes.Length,
                        reliable ? Constants.k_nSteamNetworkingSend_Reliable : Constants.k_nSteamNetworkingSend_UnreliableNoDelay, Channel);
                }
            }
            finally { handle.Free(); }
        }

        static void Receive()
        {
            int n = SteamNetworkingMessages.ReceiveMessagesOnChannel(Channel, inbox, inbox.Length);
            for (int i = 0; i < n; i++)
            {
                try
                {
                    var msg = SteamNetworkingMessage_t.FromIntPtr(inbox[i]);
                    var data = new byte[msg.m_cbSize];
                    Marshal.Copy(msg.m_pData, data, 0, msg.m_cbSize);
                    var text = Encoding.UTF8.GetString(data);
                    int split = text.IndexOf('\n');
                    if (split > 0) LuaEngine.NetMessage(text.Substring(0, split), msg.m_identityPeer.GetSteamID().ToString(), text.Substring(split + 1));
                }
                catch (Exception e) { CorePlugin.Log.LogWarning("[Net] " + e.Message); }
                finally { SteamNetworkingMessage_t.Release(inbox[i]); }
            }
        }
    }
}
