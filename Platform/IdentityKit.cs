using System;
using System.IO;
using System.Text;
using BlueprintHub.Bpc;
using Colossal.PSI.Common;
using UnityEngine;

namespace BlueprintHub.Platform
{
    /// <summary>
    /// 玩家在本机的稳定身份 —— 只用来做「点赞/套用每台机器只算一次」的去重键。
    /// 需求 5 明确：玩家账号不对外展示；本文件产出的键也从不上网络（上传走 PR，作者身份在仓库侧另算）。
    ///
    /// FACT：Colossal.PSI.Common.PlatformManager.instance.userSpecificPath 存在
    /// （research/api-PlatformManager.txt:200 => m_PrincipalUserBackend?.userSpecificPath，平台未就绪时为 null）。
    /// 所以三层兜底：登录主体路径 → 设备唯一 ID → 自己生成的随机 ID 落盘。
    /// </summary>
    public static class IdentityKit
    {
        private static string s_Cached;

        public static string PlayerKey()
        {
            if (!string.IsNullOrEmpty(s_Cached)) return s_Cached;
            string raw = null;
            try
            {
                raw = PlatformManager.instance.userSpecificPath;
            }
            catch (Exception ex) { BlueprintHubMod.log.Info("userSpecificPath 不可用：" + ex.GetType().Name); }

            if (string.IsNullOrEmpty(raw))
            {
                try { raw = SystemInfo.deviceUniqueIdentifier; }
                catch (Exception ex) { BlueprintHubMod.log.Info("deviceUniqueIdentifier 不可用：" + ex.GetType().Name); }
            }
            if (string.IsNullOrEmpty(raw)) raw = LocalId();

            s_Cached = WorkshopClient.ShortHashOf(Encoding.UTF8.GetBytes(raw));
            return s_Cached;
        }

        /// <summary>最后一层兜底：本机生成一次、写进 state/device.json，之后一直用它。</summary>
        private static string LocalId()
        {
            try
            {
                string file = Path.Combine(LocalLibrary.StateDir, "device.json");
                string text = LocalLibrary.ReadText(file);
                if (!string.IsNullOrEmpty(text))
                {
                    JsonValue v;
                    string err;
                    if (Json.TryParse(text, out v, out err))
                    {
                        string id = v.Str("id");
                        if (id.Length >= 8) return id;
                    }
                }
                string fresh = Guid.NewGuid().ToString("N");
                LocalLibrary.WriteText(file, "{\"schemaVersion\":1,\"id\":\"" + fresh + "\"}\n");
                return fresh;
            }
            catch (Exception ex)
            {
                BlueprintHubMod.log.Warn("LocalId: " + ex.GetType().Name);
                return Guid.NewGuid().ToString("N");
            }
        }
    }
}
