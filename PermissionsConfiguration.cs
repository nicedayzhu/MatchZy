using System.Collections.Generic;

namespace MatchZy
{
    /// <summary>
    /// permissions.jsonc 的模型，用于示例化和读取 MatchZy 专用权限配置。
    /// 实际权限判定仍然主要依赖 SwiftlyS2 全局权限系统。
    /// </summary>
    public class PermissionsConfiguration
    {
        /// <summary>
        /// 按玩家 SteamID 映射的权限集合，兼容 Utility.LoadAdmins 的读取逻辑。
        /// </summary>
        public PermissionsData Permissions { get; set; } = new();
    }

    public class PermissionsData
    {
        /// <summary>
        /// key = SteamID64 (string)，value = 该玩家拥有的权限列表。
        /// </summary>
        public Dictionary<string, List<string>> Players { get; set; } = new();
    }
}

