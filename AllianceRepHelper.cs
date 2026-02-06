using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace AllianceRepHelper
{
    /// <summary>
    /// Main session component for the Alliance Reputation Helper mod.
    /// Lets faction Founders/Leaders choose to align their faction with a configured
    /// NPC faction via chat command.
    /// 
    /// Usage:  /alliance FactionTag
    /// Example: /alliance SOBAN
    /// 
    /// When a faction leader aligns with a target NPC faction:
    ///   - Their faction's reputation with the chosen NPC faction is set to +AllyReputation (default 1500)
    ///   - Their faction's reputation with all other configured NPC factions is set to EnemyReputation (default -1500)
    /// 
    /// Requirements:
    ///   - The player must belong to a faction.
    ///   - The player must be a Founder or Leader of their faction.
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class AllianceRepSession : MySessionComponentBase
    {
        // -- Constants -------------------------------------------------------
        private const string CommandPrefix = "/alliance";
        private const string ConfigFileName = "AllianceRepHelper.cfg";
        private const string DataFileName = "AllianceRepHelper_FactionChoices.dat";
        private const ushort NetworkChannelId = 39471; // Arbitrary unique ID for our mod's network messages

        // -- Configuration (loaded from file, with sensible defaults) --------
        private int _allyReputation = 1500;
        private int _enemyReputation = -1500;
        private bool _allowOnlyNpcFactions = true;
        private readonly List<string> _allowedFactionTags = new List<string>();

        // -- Runtime State ---------------------------------------------------
        // Tracks which player factions have already made an alliance choice (by faction ID)
        private readonly HashSet<long> _factionsWhoHaveChosen = new HashSet<long>();
        private bool _isServer;

        // ====================================================================
        //  Lifecycle
        // ====================================================================
        public override void LoadData()
        {
            base.LoadData();

            _isServer = MyAPIGateway.Session.IsServer || MyAPIGateway.Multiplayer == null || !MyAPIGateway.Multiplayer.MultiplayerActive;

            if (_isServer)
            {
                LoadConfig();
                LoadFactionChoices();

                // MessageEnteredSender fires on the server with the sender's Steam ID
                MyAPIGateway.Utilities.MessageEnteredSender += OnMessageEnteredSender;

                // Register network handler so clients can receive chat feedback
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(NetworkChannelId, OnSecureMessageReceived);

                Log("AllianceRepHelper loaded on server. Allowed factions: " +
                 (_allowedFactionTags.Count > 0
                ? string.Join(", ", _allowedFactionTags)
                : "(none configured)"));
            }
            else
            {
                // Client side: register to receive feedback messages from server
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(NetworkChannelId, OnSecureMessageReceived);
            }
        }

        protected override void UnloadData()
        {
            MyAPIGateway.Utilities.MessageEnteredSender -= OnMessageEnteredSender;

            if (MyAPIGateway.Multiplayer != null)
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(NetworkChannelId, OnSecureMessageReceived);

            if (_isServer)
                SaveFactionChoices();

            base.UnloadData();
        }

        // ====================================================================
        //  Chat Command Handling (Server-Side)
        // ====================================================================
        private void OnMessageEnteredSender(ulong sender, string messageText, ref bool sendToOthers)
        {
            if (string.IsNullOrEmpty(messageText))
                return;

            if (!messageText.StartsWith(CommandPrefix, StringComparison.OrdinalIgnoreCase))
                return;

            // Consume the command so it doesn't show in global chat
            sendToOthers = false;

            string args = messageText.Substring(CommandPrefix.Length).Trim();

            if (string.IsNullOrEmpty(args) || args.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                SendHelp(sender);
                return;
            }

            if (args.Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                SendFactionList(sender);
                return;
            }

            if (args.Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                SendStatus(sender);
                return;
            }

            // Otherwise treat it as a faction tag to align with
            ProcessAlignCommand(sender, args);
        }

        // ====================================================================
        //  Command Implementations
        // ====================================================================
        private void SendHelp(ulong steamId)
        {
            SendChatToPlayer(steamId, "Alliance Rep Helper Commands:");
            SendChatToPlayer(steamId, "  /alliance <FactionTag>  - Align your faction with an NPC faction");
            SendChatToPlayer(steamId, "  /alliance list - Show available NPC factions");
            SendChatToPlayer(steamId, "  /alliance status        - Show your faction's current reputation");
            SendChatToPlayer(steamId, "  /alliance help       - Show this help");
            SendChatToPlayer(steamId, "Note: You must be a Founder or Leader of your faction to use /alliance.");
        }

        private void SendFactionList(ulong steamId)
        {
            var factions = GetAllowedFactions();
            if (factions.Count == 0)
            {
                SendChatToPlayer(steamId, "No alliance factions are currently configured.");
                return;
            }

            SendChatToPlayer(steamId, "Available Alliance Factions:");
            foreach (var faction in factions)
            {
                SendChatToPlayer(steamId, string.Format("  [{0}] {1}", faction.Tag, faction.Name));
            }
        }

        private void SendStatus(ulong steamId)
        {
            long identityId = MyAPIGateway.Players.TryGetIdentityId(steamId);
            if (identityId == 0)
            {
                SendChatToPlayer(steamId, "Error: Could not resolve your player identity.");
                return;
            }

            // Check if player is in a faction
            IMyFaction playerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(identityId);
            if (playerFaction == null)
            {
                SendChatToPlayer(steamId, "You are not in a faction. Join or create a faction first.");
                return;
            }

            var npcFactions = GetAllowedFactions();
            if (npcFactions.Count == 0)
            {
                SendChatToPlayer(steamId, "No alliance factions are configured.");
                return;
            }

            SendChatToPlayer(steamId, string.Format("Faction [{0}] Reputation:", playerFaction.Tag));
            foreach (var npcFaction in npcFactions)
            {
                int factionRep = MyAPIGateway.Session.Factions.GetReputationBetweenFactions(playerFaction.FactionId, npcFaction.FactionId);
                int playerRep = MyAPIGateway.Session.Factions.GetReputationBetweenPlayerAndFaction(identityId, npcFaction.FactionId);
                SendChatToPlayer(steamId, string.Format("  [{0}] {1}: faction={2}, personal={3}", npcFaction.Tag, npcFaction.Name, factionRep, playerRep));
            }

            if (_factionsWhoHaveChosen.Contains(playerFaction.FactionId))
                SendChatToPlayer(steamId, "  (Your faction has already chosen an alliance)");
            else
                SendChatToPlayer(steamId, "  (Your faction has not yet chosen an alliance)");
        }

        private void ProcessAlignCommand(ulong steamId, string factionTag)
        {
            // 1. Resolve the player's identity
            long identityId = MyAPIGateway.Players.TryGetIdentityId(steamId);
            if (identityId == 0)
            {
                SendChatToPlayer(steamId, "Error: Could not resolve your player identity.");
                return;
            }

            // 2. Check if player is in a faction
            IMyFaction playerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(identityId);
            if (playerFaction == null)
            {
                SendChatToPlayer(steamId, "You must be in a faction to use this command. Join or create a faction first.");
                return;
            }

            // 3. Check if player is a Founder or Leader of their faction
            if (!playerFaction.IsFounder(identityId) && !playerFaction.IsLeader(identityId))
            {
                SendChatToPlayer(steamId, "Only faction Founders and Leaders can set alliance reputation. Ask your faction leader.");
                return;
            }

            // 4. Find the target NPC faction
            IMyFaction targetFaction = MyAPIGateway.Session.Factions.TryGetFactionByTag(factionTag);
            if (targetFaction == null)
            {
                SendChatToPlayer(steamId, string.Format("Faction with tag '{0}' not found. Use /alliance list to see available factions.", factionTag));
                return;
            }

            // 5. Validate the target faction is an allowed NPC faction
            if (!IsFactionAllowed(targetFaction))
            {
                SendChatToPlayer(steamId, string.Format("Faction [{0}] is not available for alliance. Use /alliance list to see available factions.", targetFaction.Tag));
                return;
            }

            // 6. Prevent aligning with yourself (player faction shouldn't be an NPC faction in the list)
            if (playerFaction.FactionId == targetFaction.FactionId)
            {
                SendChatToPlayer(steamId, "You cannot align with your own faction!");
                return;
            }

            // 7. Set faction-to-faction reputations (both directions)
            //    AND set player-to-faction reputation for every member in the player's faction,
            //    since the game UI may display player-to-faction rep rather than faction-to-faction.
            var allAllowed = GetAllowedFactions();
            int memberCount = 0;
            foreach (var npcFaction in allAllowed)
            {
                int rep = (npcFaction.FactionId == targetFaction.FactionId) ? _allyReputation : _enemyReputation;

                // Faction-to-faction in both directions
                MyAPIGateway.Session.Factions.SetReputation(playerFaction.FactionId, npcFaction.FactionId, rep);
                MyAPIGateway.Session.Factions.SetReputation(npcFaction.FactionId, playerFaction.FactionId, rep);

                // Player-to-faction for every member of the player's faction
                foreach (var kvp in playerFaction.Members)
                {
                    long memberId = kvp.Key;
                    MyAPIGateway.Session.Factions.SetReputationBetweenPlayerAndFaction(memberId, npcFaction.FactionId, rep);

                    if (npcFaction.FactionId == allAllowed[0].FactionId)
                        memberCount++;
                }

                Log(string.Format("Set faction [{0}] (id:{1}) <-> [{2}] (id:{3}) reputation to {4}, plus {5} member(s) player rep (requested by steam:{6})",
                    playerFaction.Tag, playerFaction.FactionId, npcFaction.Tag, npcFaction.FactionId, rep, playerFaction.Members.Count, steamId));
            }

            // 8. Record the choice
            _factionsWhoHaveChosen.Add(playerFaction.FactionId);
            SaveFactionChoices();

            SendChatToPlayer(steamId, string.Format("Your faction [{0}] has allied with [{1}] {2}!",
                        playerFaction.Tag, targetFaction.Tag, targetFaction.Name));
            SendChatToPlayer(steamId, string.Format("  Reputation set to {0} with [{1}].", _allyReputation, targetFaction.Tag));
            if (allAllowed.Count > 1)
            {
                SendChatToPlayer(steamId, string.Format("  Reputation set to {0} with all other alliance factions.", _enemyReputation));
            }
        }

        // ====================================================================
        //  Faction Resolution Helpers
        // ====================================================================
        private List<IMyFaction> GetAllowedFactions()
        {
            var result = new List<IMyFaction>();

            if (_allowedFactionTags.Count > 0)
            {
                // Use the explicitly configured list
                foreach (string tag in _allowedFactionTags)
                {
                    IMyFaction faction = MyAPIGateway.Session.Factions.TryGetFactionByTag(tag);
                    if (faction != null && (!_allowOnlyNpcFactions || faction.IsEveryoneNpc()))
                        result.Add(faction);
                }
            }

            return result;
        }

        private bool IsFactionAllowed(IMyFaction faction)
        {
            if (_allowOnlyNpcFactions && !faction.IsEveryoneNpc())
                return false;

            if (_allowedFactionTags.Count > 0)
                return _allowedFactionTags.Contains(faction.Tag);

            return false;
        }

        // ====================================================================
        //  Network / Chat Feedback
        // ====================================================================

        /// <summary>
        /// Sends a chat message to a specific player. On dedicated servers, this uses the
        /// network channel. On single-player / listen-server, it shows directly.
        /// </summary>
        private void SendChatToPlayer(ulong steamId, string message)
        {
            if (MyAPIGateway.Utilities.IsDedicated)
            {
                // Dedicated server: send over network to the client
                byte[] data = System.Text.Encoding.UTF8.GetBytes("CHAT:" + message);
                MyAPIGateway.Multiplayer.SendMessageTo(NetworkChannelId, data, steamId, true);
            }
            else
            {
                // Single-player or listen server: show directly
                MyAPIGateway.Utilities.ShowMessage("Alliance", message);
            }
        }

        /// <summary>
        /// Client-side handler for network messages from the server.
        /// Uses the secure message handler signature: (channelId, data, senderSteamId, isFromServer).
        /// </summary>
        private void OnSecureMessageReceived(ushort channelId, byte[] data, ulong senderSteamId, bool isFromServer)
        {
            // Only process messages that came from the server
            if (!isFromServer)
                return;

            try
            {
                string text = System.Text.Encoding.UTF8.GetString(data);
                if (text.StartsWith("CHAT:"))
                {
                    string message = text.Substring(5);
                    MyAPIGateway.Utilities.ShowMessage("Alliance", message);
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("AllianceRepHelper client error: " + ex);
            }
        }

        // ====================================================================
        //  Configuration File (per-world)
        // ====================================================================

        private void LoadConfig()
        {
            try
            {
                if (MyAPIGateway.Utilities.FileExistsInWorldStorage(ConfigFileName, typeof(AllianceRepSession)))
                {
                    using (TextReader reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(ConfigFileName, typeof(AllianceRepSession)))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            line = line.Trim();
                            if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith("//"))
                                continue;

                            int eqIndex = line.IndexOf('=');
                            if (eqIndex < 0)
                                continue;

                            string key = line.Substring(0, eqIndex).Trim();
                            string value = line.Substring(eqIndex + 1).Trim();

                            switch (key.ToLowerInvariant())
                            {
                                case "allyreputation":
                                    int.TryParse(value, out _allyReputation);
                                    break;
                                case "enemyreputation":
                                    int.TryParse(value, out _enemyReputation);
                                    break;
                                case "allowonlynpcfactions":
                                    bool.TryParse(value, out _allowOnlyNpcFactions);
                                    break;
                                case "factions":
                                    _allowedFactionTags.Clear();
                                    foreach (string tag in value.Split(','))
                                    {
                                        string trimmed = tag.Trim();
                                        if (!string.IsNullOrEmpty(trimmed))
                                            _allowedFactionTags.Add(trimmed);
                                    }
                                    break;
                            }
                        }
                    }

                    Log("Configuration loaded from " + ConfigFileName);
                }
                else
                {
                    // Create a default config file so the server owner can edit it
                    SaveDefaultConfig();
                    Log("Default configuration created at " + ConfigFileName);
                }
            }
            catch (Exception ex)
            {
                Log("Error loading config: " + ex.Message);
            }
        }

        private void SaveDefaultConfig()
        {
            try
            {
                using (TextWriter writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(ConfigFileName, typeof(AllianceRepSession)))
                {
                    writer.WriteLine("# Alliance Rep Helper Configuration");
                    writer.WriteLine("# -----------------------------------");
                    writer.WriteLine("# Comma-separated list of faction tags that players can align with.");
                    writer.WriteLine("# These MUST be set for the mod to function.");
                    writer.WriteLine("# Example: Factions = SOBAN, KHAANEPH");
                    writer.WriteLine("Factions = ");
                    writer.WriteLine();
                    writer.WriteLine("# Reputation value granted to the chosen ally faction (default: 1500)");
                    writer.WriteLine("AllyReputation = 1500");
                    writer.WriteLine();
                    writer.WriteLine("# Reputation value set for all OTHER configured factions (default: -1500)");
                    writer.WriteLine("EnemyReputation = -1500");
                    writer.WriteLine();
                    writer.WriteLine("# Only allow NPC factions to be selected (default: true)");
                    writer.WriteLine("# This prevents players from using the command to force relationships with player factions.");
                    writer.WriteLine("AllowOnlyNpcFactions = true");
                }
            }
            catch (Exception ex)
            {
                Log("Error saving default config: " + ex.Message);
            }
        }

        // ====================================================================
        //  Faction Choice Persistence (per-world)
        // ====================================================================

        private void LoadFactionChoices()
        {
            try
            {
                if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(DataFileName, typeof(AllianceRepSession)))
                    return;

                using (TextReader reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(DataFileName, typeof(AllianceRepSession)))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim();
                        long factionId;
                        if (long.TryParse(line, out factionId))
                            _factionsWhoHaveChosen.Add(factionId);
                    }
                }

                Log(string.Format("Loaded {0} faction choice records.", _factionsWhoHaveChosen.Count));
            }
            catch (Exception ex)
            {
                Log("Error loading faction choices: " + ex.Message);
            }
        }

        private void SaveFactionChoices()
        {
            try
            {
                using (TextWriter writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(DataFileName, typeof(AllianceRepSession)))
                {
                    foreach (long factionId in _factionsWhoHaveChosen)
                        writer.WriteLine(factionId.ToString());
                }
            }
            catch (Exception ex)
            {
                Log("Error saving faction choices: " + ex.Message);
            }
        }

        // ====================================================================
        //  Logging
        // ====================================================================

        private static void Log(string message)
        {
            MyLog.Default.WriteLineAndConsole("AllianceRepHelper: " + message);
        }
    }
}
