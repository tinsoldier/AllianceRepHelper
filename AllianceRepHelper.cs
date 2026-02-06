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
    /// Lets players choose to align with a configured NPC faction via chat command.
    /// 
    /// Usage:  /alliance FactionTag
    /// Example: /alliance Soban
    /// 
    /// When a player aligns with a faction:
    ///   - Their reputation with the chosen faction is set to +AllyReputation (default 1500)
    ///   - Their reputation with all other configured factions is set to -EnemyReputation (default 1500)
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class AllianceRepSession : MySessionComponentBase
    {
        // -- Constants -------------------------------------------------------
        private const string CommandPrefix = "/alliance";
        private const string ConfigFileName = "AllianceRepHelper.cfg";
        private const string DataFileName = "AllianceRepHelper_PlayerChoices.dat";
        private const ushort NetworkChannelId = 39471; // Arbitrary unique ID for our mod's network messages

        // -- Configuration (loaded from file, with sensible defaults) --------
        private int _allyReputation = 1500;
        private int _enemyReputation = -1500;
        private bool _allowOnlyNpcFactions = true;
        private readonly List<string> _allowedFactionTags = new List<string>();

        // -- Runtime State ---------------------------------------------------
        private readonly HashSet<ulong> _playersWhoHaveChosen = new HashSet<ulong>();
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
                LoadPlayerChoices();

                // MessageEnteredSender fires on the server with the sender's Steam ID
                MyAPIGateway.Utilities.MessageEnteredSender += OnMessageEnteredSender;

                // Register network handler so clients can receive chat feedback
                MyAPIGateway.Multiplayer.RegisterMessageHandler(NetworkChannelId, OnClientMessageReceived);

                Log("AllianceRepHelper loaded on server. Allowed factions: " +
                 (_allowedFactionTags.Count > 0
                ? string.Join(", ", _allowedFactionTags)
                      : "(any NPC faction)"));
            }
            else
            {
                // Client side: register to receive feedback messages from server
                MyAPIGateway.Multiplayer.RegisterMessageHandler(NetworkChannelId, OnClientMessageReceived);
            }
        }

        protected override void UnloadData()
        {
            MyAPIGateway.Utilities.MessageEnteredSender -= OnMessageEnteredSender;

            if (MyAPIGateway.Multiplayer != null)
                MyAPIGateway.Multiplayer.UnregisterMessageHandler(NetworkChannelId, OnClientMessageReceived);

            if (_isServer)
                SavePlayerChoices();

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
            SendChatToPlayer(steamId, "  /alliance <FactionTag>  - Align with a faction");
            SendChatToPlayer(steamId, "  /alliance list      - Show available factions");
            SendChatToPlayer(steamId, "  /alliance status        - Show your current reputation");
            SendChatToPlayer(steamId, "  /alliance help- Show this help");
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

            var factions = GetAllowedFactions();
            if (factions.Count == 0)
            {
                SendChatToPlayer(steamId, "No alliance factions are configured.");
                return;
            }

            SendChatToPlayer(steamId, "Your Faction Reputations:");
            foreach (var faction in factions)
            {
                int rep = MyAPIGateway.Session.Factions.GetReputationBetweenPlayerAndFaction(identityId, faction.FactionId);
                SendChatToPlayer(steamId, string.Format("  [{0}] {1}: {2}", faction.Tag, faction.Name, rep));
            }

            if (_playersWhoHaveChosen.Contains(steamId))
                SendChatToPlayer(steamId, "  (You have already chosen an alliance)");
            else
                SendChatToPlayer(steamId, "  (You have not yet chosen an alliance)");
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

            // 2. Find the target faction
            IMyFaction targetFaction = MyAPIGateway.Session.Factions.TryGetFactionByTag(factionTag);
            if (targetFaction == null)
            {
                SendChatToPlayer(steamId, string.Format("Faction with tag '{0}' not found. Use /alliance list to see available factions.", factionTag));
                return;
            }

            // 3. Validate the faction is allowed
            if (!IsFactionAllowed(targetFaction))
            {
                SendChatToPlayer(steamId, string.Format("Faction [{0}] is not available for alliance. Use /alliance list to see available factions.", targetFaction.Tag));
                return;
            }

            // 4. Check if player is a member of the target -- they shouldn't be
            if (targetFaction.IsMember(identityId))
            {
                SendChatToPlayer(steamId, "You are already a member of that faction!");
                return;
            }

            // 5. Set reputations
            var allAllowed = GetAllowedFactions();
            foreach (var faction in allAllowed)
            {
                if (faction.FactionId == targetFaction.FactionId)
                {
                    MyAPIGateway.Session.Factions.SetReputationBetweenPlayerAndFaction(identityId, faction.FactionId, _allyReputation);
                    Log(string.Format("Set reputation for player {0} (steam:{1}) -> [{2}] to {3}", identityId, steamId, faction.Tag, _allyReputation));
                }
                else
                {
                    MyAPIGateway.Session.Factions.SetReputationBetweenPlayerAndFaction(identityId, faction.FactionId, _enemyReputation);
                    Log(string.Format("Set reputation for player {0} (steam:{1}) -> [{2}] to {3}", identityId, steamId, faction.Tag, _enemyReputation));
                }
            }

            // 6. Record the choice
            _playersWhoHaveChosen.Add(steamId);
            SavePlayerChoices();

            SendChatToPlayer(steamId, string.Format("You have aligned with [{0}] {1}!", targetFaction.Tag, targetFaction.Name));
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
        /// </summary>
        private void OnClientMessageReceived(byte[] data)
        {
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
                    writer.WriteLine("# Example: Factions = SOBAN, KADESH");
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
        //  Player Choice Persistence (per-world)
        // ====================================================================

        private void LoadPlayerChoices()
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
                        ulong steamId;
                        if (ulong.TryParse(line, out steamId))
                            _playersWhoHaveChosen.Add(steamId);
                    }
                }

                Log(string.Format("Loaded {0} player choice records.", _playersWhoHaveChosen.Count));
            }
            catch (Exception ex)
            {
                Log("Error loading player choices: " + ex.Message);
            }
        }

        private void SavePlayerChoices()
        {
            try
            {
                using (TextWriter writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(DataFileName, typeof(AllianceRepSession)))
                {
                    foreach (ulong steamId in _playersWhoHaveChosen)
                        writer.WriteLine(steamId.ToString());
                }
            }
            catch (Exception ex)
            {
                Log("Error saving player choices: " + ex.Message);
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
