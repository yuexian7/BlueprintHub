# Road Builder（ModId 87190）机制分析 —— BlueprintHub 的参照物

> 为什么分析它：BlueprintHub 要的是「**模组自己的数据工坊**」——一种游戏没有类目、官方商店装不下的用户内容。Road Builder 是这款游戏里**已经跑通的、最接近的先例**：它有游戏内 React 面板、有自建在线库（Discover/上传）、有自己的存档内嵌格式，而且**不依赖把内容做成模组条目**。
> 本模组的 UI 面板与数据面，按它的样子来。
>
> 取证来源（两条都独立复核过）：
> ① **本机实装**：`.cache\Mods\pdx_mods\87190_42\`（`RoadBuilder.dll` 508 KB、`RoadBuilder.mjs` 50 KB、`RoadBuilder.css` 57 KB、`0Harmony.dll` 910 KB、`Icons/`、`Badges/`、`images/`、三平台 Burst 桩）+ `ModsData\RoadBuilder\*.json`（6 个）+ `RoadBuilder.coc` + `Logs\RoadBuilder.log`。
> ② **源码**：`github.com/JadHajjar/RoadBuilder-CSII`（repo id 818301174，commit `b9fbd0f`，MIT）。下文每个结论都标 `文件:符号`。
> 证据分级沿用 `设计文档.md`：**FACT** = 读过源码或本机磁盘；**INFERENCE** = 推断；**UNVERIFIED** = 未见证据。

---

> **⚠ 先立一条边界：它的"存档内嵌"不能照抄成我们的分发方案 —— 两者处境根本不同。**
> Road Builder 之所以**必须**把配方塞进存档，是因为它造的是**自定义资产**：一条路要显示，就得有一个存在的 `NetGeometryPrefab`；资产不在，玩家看到的就是 README 里那句 "default grey networks"。它的"分享存档即分享道路"是**没办法中的办法**（云存档私有，跨用户只能靠"把整个存档当 mod 条目上传"）。
> BlueprintHub 造的是**用已有资产搭出来的布局**：我们套进去的是普通 `Net/Building/Prop` 实体（`PrefabRef` 指向游戏或别人模组里**已存在**的资产）+ 普通高程改动 + 普通区划。这些**本来就是游戏自己会存的东西**。
> → **推论三条**：① 蓝图正文**不需要**进存档，也**不需要**进任何官方平台；② 没装 BlueprintHub 的玩家打开这张存档，**照样能看到那些路和楼**（只要资产依赖齐），这是 Road Builder 没有的好性质；③ 存档内嵌对我们的价值只剩"**出处标签**"和"**跨局的整体撤销/搬移锚点**"，**不承担分发**——工坊的传输 100% 走我们自己的公共数据面。

---

## 一、它把数据存在哪（三个地方，不是一个）

这是最关键的一张表，也直接回答「官方能帮我们保存吗」。

| # | 存哪 | 键名/路径 | 谁负责同步 | 证据 |
|---|---|---|---|---|
| 1 | **本地：一条内容 = 一个 JSON 文件** | `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\RoadBuilder\{ID}.json` | 玩家手动复制文件（README 原文：*"Sharing roads is possible, but it is a manual process for now"*） | `Utilities/LocalSaveUtil.cs:Save()` + `Utilities/FoldersUtil.cs:ContentFolder`；本机实测存在 |
| 2 | **存档内：一个自定义组件把配置写进 savegame** | `NetworkConfigComponent` 实现 `Serialize/Deserialize<TWriter>`，里面转手调 `RoadBuilderSerializeSystem.SerializeNetwork/DeserializeNetwork` | **游戏自己的存档系统**（→ 云存档、分享存档都白拿） | `Systems/RoadBuilderSerializeSystem.cs` + `Domain/Components/NetworkConfigComponent.cs`；README：*"Roads are included in your save-game, allowing you to share it with anyone"* |
| 3 | **远端：一个自建极小 REST 库** | `GET /Roads?query&category&order&page`、`GET /RoadConfig/{id}.json`、`POST /SaveRoad` | 他们自己的服务器 | `Utilities/Online/ApiUtil.cs` |

**FACT · 文件名里的身份编码**
本机 6 个配置文件的实际名字：`r8d600010-…-76561198305266033.json`、`r90f7ee46-…-76561198397112589.json`。
格式 = `r` + GUID + `-` + **SteamID64**，且文件内 `"OriginalID" == "ID" == 文件名`。
→ 两个含义一起解决了：**全局唯一 ID**（不会撞车）+ **作者身份**（下载后仍保留原作者 SteamID，本机出现两个不同 ID = 确实收到过别人分享的配置）。
→ **INFERENCE**：`ID` 就是工坊的主键，服务端不需要自己发号；改名走 `DeletePreviousLocalConfig`（按 `OriginalID` 删旧文件），**ID 永不复用**。

**FACT · 一条 road config 有多大**
本机最大一份 = **4,335 B**（8 车道分隔式道路，11 条 lane，含 `GroupOptions` 若干）。字段：
`Type`(多态判别) / `Version` / `OriginalID` / `ID` / `Name` / `PillarPrefabName` / `SpeedLimit` / `MaxSlopeSteepness` / `Category` / `Addons`(**逗号拼接的一根字符串**) / `Lanes[]`{`Version`,`SectionPrefabName`,`GroupPrefabName`,**`GroupOptions`{显示名:选中值}``}。
其中 `GroupPrefabName` 是 **C# 类型全名**（如 `RoadBuilder.LaneGroups.SidewalkGroupPrefab`）→ **车道组是代码里写死的类，配置只是配方（recipe），运行时反射实例化再现场生成资产**（README：*"Roads are generated in real time, guaranteeing future compatibility with updates"*；实现在 `Utilities/NetworkPrefabGenerationUtil.cs` 27 KB + `Systems/RoadBuilderNetSectionsSystem.cs` 50 KB）。
→ **这条对 BlueprintHub 极其重要**：Road Builder 之所以每条只有 4 KB，是因为它**根本不存几何，只存配方**。我们做不到（市区布局就是几何），所以我们的载荷必须自己扛体积（见「五」）。

**FACT · 序列化格式用的是游戏自带的 `Colossal.Json`**
`JSON.Dump(config)` 写、`JSON.Load` + `JSON.MakeInto<RoadConfig>` 读；远端上传用 `JSON.Dump(config, EncodeOptions.CompactPrint)`。**没有任何第三方序列化库**（无 Newtonsoft / System.Text.Json）。
多态靠手写 `switch (json["Type"])`：`RoadConfig`/`TrackConfig`/`FenceConfig`/`PathConfig`，**未知类型 → `Mod.Log.Warn` + 返回 null**，不抛异常（`LocalSaveUtil.LoadFromJson`）；`LoadConfigs()` 对每个文件单独 `try/catch { Warn; continue; }`。
→ **可直接照抄的前向兼容三件套**：① `Type` 字段做判别；② 未知类型只 Warn 不炸；③ 逐文件 try/catch 跳过坏文件（一个坏文件不许拖垮整个库）。

**FACT · 版本迁移写在配置对象自己身上**
`RoadBuilderSerializeSystem.CURRENT_VERSION = 5`，另有 `VER_REMOVE_AGGREGATE_TYPE=2 / VER_FIX_PEDESTRIAN_ROADS=3 / VER_MANAGEMENT_REWORK=4 / VER_CHANGE_SOUND_BARRIER=5` 一组里程碑常量；反序列化后统一调 `config.ApplyVersionChanges()`。存档里也带 `ushort version`。
→ 比"整份重设计"便宜的兼容策略：**每条历史破坏性改动留一个具名常量 + 一个 if**，读老数据时逐条前滚。BlueprintHub 的 `meta.schemaVersion` 应该配一份同样的 `VER_*` 表（`设计文档.md` 4.5 现在只写了"只加字段不改语义"，不够，补这条）。

**FACT · 载入时的自愈：坏引用降级为原生资产**
`OnGameLoadingComplete` → `FixInvalidEdges()`：查所有 `Edge+PrefabRef+(Road|TrainTrack|TramTrack|SubwayTrack)`，凡是 `refs[i].m_Prefab.Index < 0` 或 `TryGetSpecificPrefab<NetGeometryPrefab>` 失败的，**先 `Mod.Log.WarnFormat("{0} invalid edges found"` 报数，再弹 `ConfirmationDialog` 问玩家**，同意后按类别 `SetComponentData(entity, new PrefabRef{ m_Prefab = Small Road / Double Train Track / … })`。
→ BlueprintHub 的「依赖缺失」处理（`设计文档.md` 5.7）照这个形状改：**报成对计数 → 先问玩家 → 降级到原生回退资产 → 绝不静默改**。这比"自动订阅"更安全，且是已验证可行的路（Road Builder 玩家实测）。

**FACT · 只在真正被用到时才落盘**
`Mod.Settings.SaveUsedRoadsOnly` 为真时，只有 `CreateNetworksList()`（扫全域 `Edge+PrefabRef` 找出的、本图真放过的 ID）里的配置才写本地文件。
→ 对应 BlueprintHub：**"这个存档用过的蓝图"才写盘**，否则玩家编辑器的每次尝试都会往 `ModsData` 里堆垃圾文件。（README 也承认不做这件事的后果：*"you might run into an issue where road configurations are duplicated"*。）

**FACT · 存档内嵌的确切机制（原 R11，本轮解除）**
它的全部"把数据塞进存档"的代码就是两个东西：

```csharp
// RoadBuilder/Domain/Components/NetworkConfigComponent.cs（全文 21 行）
public struct NetworkConfigComponent : IComponentData, ISerializable {   // ← Colossal.Serialization.Entities.ISerializable
    public FixedString4096Bytes NetworkId;                                // Burst 安全串
    public void Deserialize<TReader>(TReader reader) => RoadBuilderSerializeSystem.DeserializeNetwork(reader);
    public void Serialize<TWriter>(TWriter writer)   => RoadBuilderSerializeSystem.SerializeNetwork(writer, NetworkId.ConvertToString());
}
```
```csharp
// RoadBuilderSerializeSystem（注册成 UpdateBefore<...>(SystemUpdatePhase.Serialize)）的 OnUpdate：
foreach (var prefabName in CreateNetworksList()) {            // 扫全域 Edge+PrefabRef 得出"本图真用到的 ID"
    var entity = EntityManager.CreateEntity();
    EntityManager.AddComponentData(entity, new NetworkConfigComponent { NetworkId = prefabName });
}
// 写：writer.Write(CURRENT_VERSION/*ushort*/); writer.Write(config.GetType().Name); writer.Write(config);
// 读：reader.Read(out ushort version); reader.Read(out string type); reader.Read(config); → 按 type switch → ApplyVersionChanges() → AddPrefab
```
为什么这样就够了：游戏在**加载模组程序集时**就已经把模组类型注册进序列化系统 —— `ModManager.ModInfo.AfterLoadAssembly` 里两行：`TypeManager.InitializeAdditionalTypes(assembly);` + `GetOrCreateSystemManaged<SerializerSystem>().SetDirty();`（本机反编译 `Game.Modding/ModManager.cs:147-151`）。
→ **对 BlueprintHub 的意义**：`IComponentData + ISerializable` 的**一个只带该组件的实体 = 存档里的一个数据槽**。**不需要 Harmony、不需要 patch 存档管线、不需要把字节藏进别人的组件。** 这是"官方帮我们存"那条通道真正可用的形态，而且是零风险形态（不动游戏任何代码路径）。

---

## 二、它怎么做游戏内面板（这条我们几乎要原样抄）

**FACT · 面板不是 IMGUI，是官方 React/Cohtml 模组**
`UI/package.json`：`react ^18.2` + `react-dom` + `typescript` + `webpack 5` + `sass`，脚本里 `update: npx create-csii-ui-mod update` → **官方 UI 脚手架**；产物 `RoadBuilder.mjs`(50 KB) + `RoadBuilder.css`(57 KB) 落进模组目录（本机 `.cache/Mods/pdx_mods/87190_42/` 里有）。
→ 我们自己的 `BusLineAutoStops/Frontend`（`moduleRegistry.append("GameTopLeft", …)` + webpack）是同一族做法；BlueprintHub 的浏览墙比那颗按钮复杂得多，**必须走 React 路线**，用 Road Builder 的目录结构（`UI/src` + `UI/tools` + `UI/types` + `tsconfig.json`）。

**FACT · C#↔JS 的桥：一个自写的 `ExtendedUISystemBase`，两种原语**
`Systems/UI/ExtendedUISystemBase.cs`(10 KB) 上派生出 4 个 UI 系统，`OnCreate` 里只用两件事：
- **`CreateBinding<T>(名字, 初值)` → `ValueBindingHelper<T>`**：**C# 是数据的唯一真值源**，赋 `.Value` 就刷新界面（`Loading`、`ErrorLoading`、`Uploading`、`CurrentPage`、`MaxPages`、`Items`、`SelectedRoadOptions`…）。
- **`CreateTrigger<T…>(名字, handler)`**：JS 调回来（`Discover.SetPage`、`Discover.SetSorting`、`Discover.SetSearchQuery`、`Discover.Download`、`Management.RoadOptionClicked(int,int,int)`、`Management.SetRoadName`…）。多参数用泛型变元，不用对象。
界面文案给的是**本地化键**而不是成品串（`"RoadBuilder.ShowInToolbarState[{枚举值}]"`），图标给的是 **`coui://` URL**（`coui://roadbuildericons/RB_Upload.svg`）。
→ 这条比 BusLineAutoStops 现在的裸 `CallBinding` 干净一个量级，**BlueprintHub 直接采用 `CreateBinding/CreateTrigger` 这套抽象**（自己写一份同形的 `ExtendedUISystemBase`，不要引它的代码，MIT 可引但要标）。

**FACT · 图片怎么进 Cohtml：注册 host location，包括临时目录**
`Mod.OnLoad`：
```csharp
UIManager.defaultUISystem.AddHostLocation("roadbuilderthumbnails", FoldersUtil.TempFolder, true);   // 可写、每局清空
UIManager.defaultUISystem.AddHostLocation("roadbuildericons", Path.Combine(Path.GetDirectoryName(asset.path), "Icons"), false);
```
取自身安装目录靠 `GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset)`；`OnDispose` 里 `RemoveHostLocation` + 删临时目录。
→ **这是"工坊浏览墙要有 500 张缩略图"的零成本答案**：不需要图床。

**FACT · 缩略图是 **SVG 源码，跟着列表一起传过来**，不是图片 URL**
`ApiUtil.GetEntries` 返回的每条 `RoadBuilderEntry{ id, name, author, tags, category, downloads, icon, uploadTime }` 里 **`icon` 就是 SVG 文本**；`LoadPage()` 把 `item.icon` 用 `File.WriteAllText` 落到 `TempFolder/{id}.svg`，界面里写 `Thumbnail = $"coui://roadbuilderthumbnails/{id}.svg"`。上传侧对称：`new ThumbnailGenerationUtil(...).GenerateSvg()` → `Encoding.UTF8.GetString(memory.ToArray())` 当 `Icon` 字段 POST 上去。
→ **BlueprintHub 的预览就该这么做，而且更适合我们**：蓝图预览本质是**俯视矢量图**（路网线段 + 地块矩形 + 区界多边形），用 SVG 生成比截图小两个数量级，还天然无损、可换色、跨分辨率。省下整个图像存储层。
⚠ 代价（要防）：SVG 文本混在列表 JSON 里 → **一页 20 条会连带 20 份缩略图**，浏览页带宽被缩略图吃掉。对策见「五」。

**FACT · 网络与线程纪律（可抄的 + 别抄的）**
可抄：`StartLoad()` 先 `cancellationTokenSource.Cancel()` 再 new 一个 → `Task.Run(LoadPage)` → 里面 `await Task.Delay(250)` 做**输入防抖**，每个 await 点后检查 `token.IsCancellationRequested`；`try/catch` 里 `Loading=false; ErrorLoading=true`（**错误是状态位不是弹窗**）；后台 `Task.Run(UploadRoad)` 配 `Uploading` 状态位；一次性引导 `MainThreadDispatcher.RegisterUpdater(() => Task.Run(PdxModsUtil.Start))`。
**别抄**（`ApiUtilBase.cs`）：① 每次请求 `using var httpClient = new HttpClient()`（连接耗尽 + 端口吃紧）；② **完全没设超时、没接 CancellationToken 到 HTTP 层** → 直接违反 Playbook 硬规则 16「任何外部 I/O 都要显式收紧超时」，玩家退出时点了没反应就是这形状；③ 失败只 `Warn(ReasonPhrase)`，没有 429/403 的区分 → 违反 Playbook「瞬时失败与永久拒绝必须分开」（`IsPermanentStatus`）。

---

## 三、身份与"谁传的"：不注册账号，白拿 Paradox 身份

**FACT · 反射进 `m_SDKContext` 取完整 PDX SDK 上下文**
```csharp
_pdxPlatform = PlatformManager.instance.GetPSI<PdxSdkPlatform>("PdxSdk");
_context = typeof(PdxSdkPlatform).GetField("m_SDKContext", BindingFlags.NonPublic | BindingFlags.Instance)
             .GetValue(_pdxPlatform) as IContext;   // PDX.SDK.Contracts.IContext
...
CurrentPlayset = _context.Mods.GetActivePlaysetId();
UserId = (await _context.Profile.Get()).Social?.DisplayName;
```
（`Utilities/PdxModsUtil.cs`，全文件 1636 B。）
→ **这条推翻我上一版设计文档里的一个限制判断**：原 FACT 3 说"模组拿不到全站搜索、只能拿官方包装给的 `ListAllModsByMe`"。**有了 `_context.Mods`（`PDX.SDK.Contracts`）就能自己构造 `SearchData`**（`OnlyModsByMe` 只是官方包装写死的一个字段）。**UNVERIFIED（新增 R10）**：裸 `IContext.Mods.Search` 用 `OnlyModsByMe=false` + 标签过滤能否搜到别人的条目、服务端是否对非浏览器调用限流。
→ 同时它给出**身份来源**：`USER_ID` = 玩家 Paradox 显示名，`IDENTIFIER` = `PlatformManager.instance.userSpecificPath`（按用户隔离的稳定路径串），上传按钮只在 `!string.IsNullOrEmpty(PdxModsUtil.UserId)`（= 已登录）时出现。**我们不需要做账号系统**，这正是"不花钱"的关键一半。

**FACT · 用 playset 给用户内容做"可见性分区"**
`config.Playsets` 是个列表，`AddToPlayset/RemoveFromPlayset` 往里面加 `PdxSdkUtil.CurrentPlayset` 或它的**取负形式** `$"-{playset}"`；界面项 `Disabled = roadBuilderRoadTrackerSystem.UsedNetworkPrefabs.Contains(SelectedRoad)`（**正在用的不许移**）。设置里还有 `NoPlaysetIsolation` 总开关。
→ BlueprintHub 需要同样的东西：从工坊下的蓝图默认只挂进**当前 playset**，否则会污染玩家其他存档的资产列表（这是我们这套工具一定会撞上的同一个问题，直接抄结论）。

---

## 四、它对"官方能不能帮我们存"给出的答案

把上面三节合起来读，Road Builder 实际是这么回答的：

1. **官方存储它只用了一个地方：存档。** 自定义组件 `NetworkConfigComponent` 把配方塞进 savegame，于是"分享内容"退化成"分享存档"，而**存档本身在 Paradox Mods 是有类目的**（README「Sharing saves on Paradox Mods」：上传存档后要手动把 Road Builder 设为 *Required Mods*）。官方帮我们同步、帮我们分发、帮我们鉴权，**但只帮我们存"跟着存档走"的东西**。
   ⚠ **这条是它的被迫选择，不是通用结论**：因为它造的是自定义资产（资产不在就渲染成灰路），配方必须跟着存档走才能显示。**我们造的是普通实体 + 普通高程，游戏自己就会存**，所以我们**不需要**用存档当分发通道 —— 云存档按账号私有，**我们读不到任何别人的云存档**，它本来也当不了工坊。详见本节开头那条边界。
2. **官方没有、也不可能有"蓝图"这个类目** —— 你的判断是对的。平台侧内容类型是"模组条目 + 内容包（`.cok`）"，粒度是"一个条目一次上传"，把每张蓝图当条目：要占条目、要过流水线（`设计文档.md` FACT 2/R3）、没有搜索与排序、也没有"某作者的全部蓝图"这种聚合。**这条路不该做主路。**
3. **检索层必须自己来，但它小得惊人。** Road Builder 的整个工坊后端就是 **3 个端点 + 一个能存 4 KB 文档、能按 name/category/下载数查询的 KV**。这不是"要养一台服务器"的量级——是可以放在免费额度里的东西（见「五」）。
4. **`DataStorage.CloudSave` 那条官方通道它没碰**（我们也不必）：游戏层 `IsCloudSupported() => false`、`GetQuota() => (-1,-1)`（`research/PdxSdkPlatform.cs:2128,2133`），跨用户可达性无证据。**UNVERIFIED（R7 保留，降级为"仅个人草稿同步候选"）。**

---

## 五、对 BlueprintHub 的具体改动（把这套模型放大到"市区"尺度）

Road Builder 的对象是 **4 KB 配方**；我们的对象是 **KB～13 MB 几何**（`设计文档.md` 4.4）。形状能抄，尺度必须重新设计，差别全在**"索引轻、载荷重"**这一条上：

| 维度 | Road Builder | BlueprintHub 采用 | 为什么 |
|---|---|---|---|
| 条目粒度 | 一条路 = 一个 JSON | **一个蓝图 = 一份 `meta.json`（≤8 KB）+ 若干 `payload/<sha16>.bin`（分节）** | 载荷要能懒加载、能按节增量、能内容寻址永久缓存 |
| 预览 | 列表里内联 SVG 源码 | **列表只给 `preview` 的 URL/哈希，SVG 单独取 + 本地缓存**；仍**不用图床**（我们生成 SVG，不是位图） | 一页 20 条 × 20 份 SVG 会把浏览页撑肥；蓝图 SVG 比路段 SVG 大得多 |
| 检索 | 自建 REST（`/Roads?query&category&order&page`） | **同一形状，但落在免费静态托管上**：`catalog/index.json` + `catalog/<slug>/p<页码>.json`（分页小文件，CDN 可缓存） | 读侧根本没有"服务器"，只有一个仓库 |
| 写入 | `POST /SaveRoad`（带 `API_KEY`+`USER_ID`+`IDENTIFIER` 头） | **两个可选写路径，读侧格式完全一致**：① PR 模式（作者的 `.bpk` 提交进公开仓库，我们是审核者）；② 一个免费 serverless 写函数（Worker/Deno/Vercel）把 JSON+bin 落到仓库/KV | 这是唯一一处"要么我们当审核、要么跑一段函数"的取舍，需要用户拍板 |
| 载荷放哪 | 服务器 DB 里一个字符串列 | **git 仓库里的文件 + CDN**（最坏 13 MB，远低于单文件 25 MB 上限）；`payload` 文件名 = 内容哈希 → 天然去重、天然不可变 | 不花钱且不需要我们能扩容 |
| ID | `r{guid}-{SteamID64}` | **`b{guid}-{SteamID64}` + `payload` 用 sha256 前 16 位** | 照抄它"作者身份写进主键"的做法，附带防伪造归属 |
| 存档内嵌 | `NetworkConfigComponent` 塞进 save，**这就是它的分享手段** | **定位不同**：我们只写 `出处标签 + 套用归属`（可选做），**分发 100% 走数据面**；配 `SaveUsedBlueprintsOnly` 同义开关 | 见本节开头那条边界：我们的产物是普通实体，游戏自己就会存 |
| 依赖缺失 | 报数 → 弹窗 → 降级为原生路 | **完全照抄**（`设计文档.md` 5.7 改口径：先降级 + 询问，自动订阅只作为可选按钮） | 已被它的玩家群体验证；也符合"先问玩家再改数据"的自家纪律 |
| 版本迁移 | `CURRENT_VERSION` + `VER_*` + `ApplyVersionChanges()` | **照抄**（补进 `设计文档.md` 4.5） | 都市天际线2 每次更新都可能碎，逐条前滚比整份重做便宜 |
| 可见性分区 | `Playsets` 列表 + `-<playset>` 取负 | **照抄** | 同一个问题一定会出现 |
| 本地化 | 随包 `Locale.json` + `LocaleHelper` + Crowdin（`crowdin.yml`） | 采用；12 语言口径不变（Playbook 3.3） | — |

**明确不抄**：`HttpClient` 每次新建、无超时、无 429/永久拒绝区分（见「二」的"别抄"）。

---

## 六、下一步要做的取证（新增，优先级从高到低）

1. **R10**：裸 `IContext.Mods.Search`（`OnlyModsByMe=false` + `Tags` + `PageSize`）能否搜到别人的条目。判据：拿到 ≥1 条非自己的 mod 及其 `Tags`。**这条成了，「不花钱的浏览层」有一个候选是"平台自己的搜索"，静态仓库退为镜像与聚合。**取证动作：照 `PdxModsUtil.cs` 那两行反射写探针，日志打结果，不开浏览器不开新账号。
2. **R1**：`Publish` 探针（**不可逆，需单独点头**）——现在它只剩"要不要把载荷塞进 mod 条目当镜像"的价值，不再是主路，**优先级降为 P3，可以不做**。
3. 读 `Systems/UI/ExtendedUISystemBase.cs` 全文（10 KB），确认 `CreateBinding` 内部是 `AddBinding(new ValueBinding<…>)` 的哪一种、以及**从后台线程赋 `.Value` 是否安全**（Road Builder 就是直接赋的，它没炸不代表线程安全）。判据：我们自己实现时给"跨线程赋值"加一条断言或统一走 `MainThreadDispatcher`。
4. ~~读 `NetworkConfigComponent.cs` 找注册口~~ **已解除（本轮之后）**，见下面「一」末尾的 **FACT · 存档内嵌的确切机制**。剩下唯一要实机验的是"没有本模组的玩家载入带我们组件的存档会怎样"（Road Builder README 自述：*"all custom roads will load in as default grey networks which you can replace with base game roads"* —— 即**能载入、会降级**，这本身就是我们要的行为先例）。
5. `LaneGroups/` 目录下的 20+ 个类（`SidewalkGroupPrefab` 等）→ 我们的"可复用布局模块"（路口模板、街区模板）若走代码配方路线，形状与它同构；但蓝图是几何不是车道组合，**只借它的"类型全名当配方键"这一招，不借它的类层次**。
