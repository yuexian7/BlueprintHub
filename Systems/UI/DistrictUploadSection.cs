using System;
using System.Globalization;
using BlueprintHub.Bpc;
using BlueprintHub.Workshop;
using Colossal.UI.Binding;
using Game.UI.InGame;
using Unity.Entities;

namespace BlueprintHub.Systems.UI
{
    /// <summary>
    /// 市辖区面板底部那一排里的「上传蓝图」条目（需求 2）。
    ///
    /// 官方做法，零 Harmony：继承 <c>Game.UI.InGame.InfoSectionBase</c> 并把自己
    /// <c>AddBottomSection(...)</c> 进 <c>SelectedInfoUISystem</c>。
    /// FACT（research/dumps/Game_UI_InGame_SelectedInfoUISystem.cs）：
    ///  · <c>public void AddBottomSection(ISectionSource section)</c>（:255）—— 底部那一排正是 ActionsSection（删除键）所在的 footer；
    ///  · <c>public Entity selectedEntity</c>（:111）与 <c>selectedPrefab</c>（:113）是选中项的唯一入口；
    ///  · InfoSectionBase.Write() 用 <c>writer.TypeBegin(GetType().FullName)</c> 把自己交出去
    ///    （research/dumps/Game_UI_InGame_InfoSectionBase.cs:123-140）⇒ 前端必须用**本类全名**当键注册组件：
    ///    moduleRegistry.extend("game-ui/game/components/selected-info-panel/selected-info-sections/selected-info-sections.tsx",
    ///    "selectedInfoSectionComponents", m =&gt; ({...m, "BlueprintHub.Systems.UI.DistrictUploadSection": Comp}))
    ///    —— 这条键名口径有已上架模组 AdvancedBuildingControl 的同款用法可对照（它的 .mjs 里就是 map["AdvancedBuildingControl.Systems.SIP_ABC"]）。
    ///
    /// 本文件只搬数据与状态，不产出任何玩家可见句子（句子全在 Locale.cs 的 DUPLOAD_* 词条里）。
    /// </summary>
    public sealed partial class DistrictUploadSection : InfoSectionBase
    {
        /// <summary>前端注册组件用的键 = 本类全名。改类名/命名空间必须同时改 UI/src/district.tsx。</summary>
        public const string kTypeName = "BlueprintHub.Systems.UI.DistrictUploadSection";

        private string m_Name = string.Empty;
        private long m_AreaU;
        private bool m_HasArea;

        protected override string group
        {
            get { return "BlueprintHub"; }
        }

        protected override void Reset()
        {
            m_Name = string.Empty;
            m_AreaU = 0L;
            m_HasArea = false;
        }

        /// <summary>
        /// 只有「选中的是一个市辖区」时才亮起来：visible 是基类交给 Visible() 的开关，
        /// Destroyed / OutsideConnection / UnderConstruction / Upgrade 四种情形基类已经替我们挡掉了。
        /// </summary>
        protected override void OnProcess()
        {
            Entity e = selectedEntity;
            if (e == Entity.Null || !EntityManager.HasComponent<Game.Areas.District>(e))
            {
                visible = false;
                UploadService.ReadyDistrict = Entity.Null;   // 选中的不是区：上传目标清空，按钮也不会出现
                return;
            }
            visible = true;
            UploadService.ReadyDistrict = e;

            try { m_Name = m_NameSystem != null ? (m_NameSystem.GetRenderedLabelName(e) ?? string.Empty) : string.Empty; }
            catch (Exception) { m_Name = string.Empty; }

            m_AreaU = 0L;
            m_HasArea = false;
            try
            {
                if (EntityManager.HasBuffer<Game.Areas.Node>(e))
                {
                    DynamicBuffer<Game.Areas.Node> ring = EntityManager.GetBuffer<Game.Areas.Node>(e, true);
                    int n = ring.Length;
                    if (n >= 3)
                    {
                        double[] xs = new double[n];
                        double[] zs = new double[n];
                        for (int i = 0; i < n; i++) { xs[i] = ring[i].m_Position.x; zs[i] = ring[i].m_Position.z; }
                        double area = Math.Abs(PolygonArea(xs, zs));
                        m_AreaU = CatalogKit.U((long)Math.Round(area));
                        m_HasArea = true;
                    }
                }
            }
            catch (Exception) { m_HasArea = false; }
        }

        public override void OnWriteProperties(IJsonWriter writer)
        {
            writer.PropertyName("type");
            writer.Write("district-upload");

            writer.PropertyName("districtName");
            writer.Write(m_Name);

            writer.PropertyName("areaU");
            writer.Write(m_HasArea ? m_AreaU.ToString(CultureInfo.InvariantCulture) : string.Empty);

            // 上传状态整段透传：按钮文案、进度、结果路径、缺采计数全在这里（UploadService 是唯一真值源）
            writer.PropertyName("phase");
            writer.Write(UploadService.PhaseName);

            writer.PropertyName("detail");
            writer.Write(UploadService.Detail ?? string.Empty);

            writer.PropertyName("draftPath");
            writer.Write(UploadService.DraftPath ?? string.Empty);

            writer.PropertyName("bpId");
            writer.Write(UploadService.BpId ?? string.Empty);

            writer.PropertyName("missing");
            writer.Write(UploadService.MissingCount.ToString(CultureInfo.InvariantCulture));

            writer.PropertyName("bytes");
            writer.Write(UploadService.TotalBytes.ToString(CultureInfo.InvariantCulture));

            writer.PropertyName("loggedIn");
            writer.Write(Platform.AccountKit.LoggedIn ? "1" : "0");
        }

        private static double PolygonArea(double[] xs, double[] zs)
        {
            int n = xs.Length;
            double a = 0d;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                a += xs[i] * zs[j] - xs[j] * zs[i];
            }
            return a * 0.5d;
        }
    }
}
