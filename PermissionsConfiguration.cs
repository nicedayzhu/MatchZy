using System.Collections.Generic;

namespace MatchZy
{
    /// <summary>
    /// Model for permissions.jsonc, used to instantiate and read MatchZy-specific permission configurations.
    /// Actual permission determination still primarily relies on SwiftlyS2's global permission system.
    /// </summary>
    public class PermissionsConfiguration
    {
        /// <summary>
        /// Permission collection mapped by player SteamID, compatible with Utility.LoadAdmins reading logic.
        /// </summary>
        public PermissionsData Permissions { get; set; } = new();
    }

    public class PermissionsData
    {
        /// <summary>
        /// key = SteamID64 (string), value = list of permissions owned by that player.
        /// </summary>
        public Dictionary<string, List<string>> Players { get; set; } = new();
    }
}

