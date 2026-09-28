# Blueprint Hub（蓝图工坊）· 都市天际线2 模组

把一块**市辖区级别的布局**（道路、地形、建筑、设施、功能区）导出成「蓝图」分享到工坊，也能把别人的蓝图套进自己的地图。

**工作原理（一句话版）**：蓝图不是存档、不是资产包，而是**配方** —— 上传时把区域内的实体裁成五节自定义二进制（terrain / nets / objects / zones / areas）加一份 `meta.json` 清单；套用时按中心点与相对高程在目标地图上**造出普通游戏实体**，之后那张地图归游戏自己管。

- 工坊数据面（公开只读）：<https://github.com/yuexian7/blueprinthub-workshop>
- 读侧地址：冷启动三镜像**并发探一次取最快**（Pages / raw / jsDelivr），之后失败按序降级；内容寻址的 blob 在任何镜像都能自校验

## 安装
1. Paradox Mods 订阅（当前为 Private，测试者由作者加白）；或
2. 本地：`dotnet build -c Release` 后自动部署到 `%LOCALLOW%\Colossal Order\Cities Skylines II\Mods\BlueprintHub\`

## 设置项（游戏选项 → 模组）
| 项 | 默认 | 说明 |
|---|---|---|
| 面板透明度 | 80% | 0~100%，1% 一档；调的是面板**背景**，卡片与文字不受影响 |
| 打开 / 关闭蓝图工坊 | 未绑定 | 默认空，由玩家自己设；左上角那颗按钮常驻可用 |
| 关于 | — | 版本 / 作者 yuexian / Ko-fi / 论坛 / RAINBOW 官网 |

> 0.2.0 那次「透明度完全没生效」的根因与修法写在 `Setting.cs` 的滑条注释里：框架给 FloatSlider 的
> `min/max/step` 是**原样**交给界面的，只有值会乘 `scalarMultiplier` —— 量程写成了内部刻度，滑块就落在量程外，
> 拖到哪都被下限夹住。现在与游戏自己的 `InterfaceSettings.interfaceTransparency` 逐字段一致。

## 构建
```bash
npm --prefix UI ci && npm --prefix UI run build   # 前端：UI/dist/BlueprintHub.mjs（改 UI/src 后要重跑）
dotnet build -c Release                           # 模组：0 错误；后处理补三平台桩，并把 .mjs + mod.json 一起部署
dotnet run --project tests/t3 -c Release          # 离线 T3：纯逻辑断言（零游戏 DLL、零网络）
node tools/check-copy.mjs                         # 文案门禁：三份词条表键集一致 + 前端零硬编码文案
node tools/dev-preview.mjs                        # 无头实拍面板：http://127.0.0.1:8765/（?demo=15 造布局）
```
数据面（改契约时要跑）：
```bash
cd ../blueprinthub-workshop && npm ci && npm run check     # 校验；npm run build 重建 catalog
```

## 面板里的数据从哪来（0.3.0 起没有内置模板）
面板**只显示工坊里的真蓝图**。工坊是空库时面板就是空态，这是正常状态，不是坏了 —— 官方测试模板由作者自己在游戏里导出上传（M3）。
开发期想造一批假条目只为拍布局，两条路，都必须显式表态：
```bash
node tools/seed-dev-catalog.mjs --yes-dev-data    # 往 workshop 灌假蓝图 → 跑真 builder → 产物落本机 dev 覆盖目录
node tools/seed-dev-catalog.mjs --clean           # 删掉覆盖，回到真实三镜像
node tools/dev-preview.mjs                        # 不起游戏、不写任何仓库：预览台 ?demo=15
```
覆盖目录 = `%LOCALLOW%\Colossal Order\Cities Skylines II\ModsData\BlueprintHub\dev-catalog\`，
只有本机有该目录时生效，玩家机器上不存在；生效时面板顶栏会挂 `测试数据` 标签，日志里也会播报一次。

## 目录
```
Bpc/         纯逻辑：JSON、分类/面积档/排序/相关度/镜像轮转/失败分类/质心/相对高程/ID/词条键 —— 一个游戏类型都不碰，可离线测
Workshop/    浏览态（拉 index → 拉分页 → 本地筛选排序分页 → 封面预热 → 本机去重计数）
Platform/    数据面客户端（复用 HttpClient、12s 超时、三镜像并发探路、哈希复核）、本地目录口径、本机身份
Systems/UI/  Cohtml 桥：一条 GetState（C# 是唯一真值源）+ 一条 Cmd（前端只回报动作）
UI/          React + webpack 面板源码（入口按钮直接用原版 Button variant="floating"）
Locale.cs    全部玩家可见文案：选项页 + 面板 61 条 × zh-HANS / zh-HANT / en-US，其余 9 个官方语言回退 en
tools/       check-copy.mjs 文案门禁 · dev-preview.mjs 预览台 · seed-dev-catalog.mjs 开发覆盖
tests/t3/    离线回归壳：与 Bpc/ 共用同一份源码，不写桩不复制
(同级仓库) ../blueprinthub-workshop/   公共数据面：schema、catalog 生成器、容量与限速实据
research/    反编译与取证产物（不进 git、不编进产物）
```

## 文案为什么绕一大圈（需求 4 的架构决定）
桥层 JSON 里**没有任何成品句子**，只有词条 slug 与数字；句子在游戏本地化词典里（`Locale.BuildPanelMap` 注册 12 个语言），
前端用 `cs2/l10n` 的 `useLocalization().translate(key, fallback)` 取词。三个理由：
1. 键形如 `Options.BLUEPRINT_HUB.<SLUG>[BlueprintHub.BlueprintHub.BlueprintHubMod.Panel.<SLUG>]`，
   BridgeTheLanguageGap 会把 identifier 命中模组 setting.id 的词条归入「模组选项及说明」→ 玩家侧十种语言由它机翻，我们不必自己交。
2. 前端硬编码中文 = 翻不动（TrafficToolEssentials 的面板就是这个毛病），所以 `tools/check-copy.mjs` 直接把这条钉成门禁。
3. `useLocalization()` 返回的是 `{translate, unitSettings}` 而**不是**扁平词典（游戏 UI bundle 里 `dc()` 的实现）——
   按 `bag[key]` 取值取不到东西，这个坑在兄弟模组的旧写法里踩过一次。

## 兼容性
零 Harmony：不与任何模组抢补丁、不引入共享 `0Harmony.dll` 的版本抢占问题。唯一可能引入 Harmony 的地方是市辖区面板那颗上传按钮（M3），届时按既有模组的方式带 2.3.3 并单独说明影响范围。

## 已定死的机器事实（细节别靠记忆）
| 结论 | 证据 |
|---|---|
| `UISystemBase.AddUpdateBinding` 存在，`OnUpdate` 主线程逐个 `Update()` | `ilspycmd Game.dll -t Game.UI.UISystemBase` |
| `GetterValueBinding.Update()` 只在视图 active 时调 getter，值不变不推 JS | `research/UIBinding/Colossal.UI.Binding.decompiled.cs:2291` |
| `CallBinding` 走 `view.BindCall`，`TriggerBinding` 走 `RegisterForEvent` | 同文件 :639 / :1997 |
| `AddHostLocation(host, path, shouldWatch=true, priority=0)` | `ilspycmd Colossal.UI.dll -t Colossal.UI.UISystem` :259 |
| 前端可用槽位：Menu / Editor / Game / GameTopLeft / GameTopRight / GameBottomLeft / GameBottomRight / UniversalModMenu | `Cities2_Data/Content/Game/UI/index.js` 里 `name:` 字面量枚举 |
| `RegisterInOptionsUI` 只在世界为 null 时失败，且对模组是 `void`（静默） | `decompiled/Game/Settings/Setting.cs:179`、`Game/Modding/ModSetting.cs:46` |
| `PlatformManager.instance.userSpecificPath` 平台未就绪时为 null | `research/api-PlatformManager.txt:200` |
| `Colossal.UI.Binding` 里也有 `JsonWriter`（与自写的那个撞名） | 编译期 CS0104 实录 |
| 游戏 Managed 里**没有** `Colossal.Json.dll`，只有 `Newtonsoft.Json.dll` | 列目录实测 |
| FloatSlider：`getter = property × scalarMultiplier`、`setter = property = ui ÷ scalarMultiplier`，而 `min/max/step` 原样交给界面 | `decompiled/Game/UI/Menu/AutomaticSettings.cs:1333` |
| 原版强调色 `--accentColorNormal:#4bc3f1`、文本 `--normalTextColor:#F0FBFF`、正/警/负 `#8bdb46 / #ffa42d / #e95f4a` | `Cities2_Data/Content/Game/UI/index.css` |
| `useLocalization()` → `{translate(key,fallback), unitSettings}`，不是扁平词典 | 游戏 UI bundle `index.js` 里 `dc()`（偏移 ≈433500） |
| 官方术语：District=市辖区、Zone=功能区、Prop=设施、Industrial=工业、Map Tile=区块、Sort by=排序方式、UI Transparency=UI透明度 | `ToolModeMemory/research/locale/zh_en.json`（官方语言包导出） |

## 当前状态
**0.3.0**（M0 + M1 完成，M1.5 的实机反馈已修）：工程骨架、选项页、只读数据面客户端、Cohtml 面板壳与真实浏览
（7 类、搜索、面积档、5 种排序、5×3 分页、点赞/套用本机去重、封面 coui、加载中/空/无结果/失败四态）；
0.3.0 这轮改的是**上手观感与接线**：入口按钮换成原版蓝底方块（白色区划图案）、透明度滑条真正生效（默认 80%）、
右上角改成图标按钮、卡片封面加大间距收紧、全部玩家可见文案重写成官方语言包口径并走词条、内置假蓝图全部移除。
M2/M3 待做：蓝图详情与套用、**采集导出 + 上传**（这才是"流程打通"的另一半）、草稿箱、账号页。
桥接口形状已冻结（`GetState` + `Cmd`），M2/M3 只在 JSON 里加字段、在 `OnCmd` 里加分支，不加新绑定。
