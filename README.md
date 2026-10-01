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
| 面板透明度 | 90% | 0~100%，1% 一档；调的是面板**背景**，卡片与文字不受影响（0.4.0 把默认从 80% 提到 90%） |
| 打开 / 关闭蓝图工坊 | 未绑定 | 默认空，由玩家自己设；左上角那颗按钮常驻可用 |
| 关于 | — | 版本 / 作者 yuexian / Ko-fi / 论坛 / RAINBOW 官网 |

> 0.2.0 那次「透明度完全没生效」的根因与修法写在 `Setting.cs` 的滑条注释里：框架给 FloatSlider 的
> `min/max/step` 是**原样**交给界面的，只有值会乘 `scalarMultiplier` —— 量程写成了内部刻度，滑块就落在量程外，
> 拖到哪都被下限夹住。现在与游戏自己的 `InterfaceSettings.interfaceTransparency` 逐字段一致。

## 构建
```bash
npm --prefix UI ci && npm --prefix UI run build   # 前端：UI/dist/BlueprintHub.mjs（改 UI/src 后要重跑）
dotnet build -c Release                           # 模组：0 错误；后处理补三平台桩，并把 .mjs + mod.json + images/ 一起部署
dotnet run --project tests/t3 -c Release          # 离线 T3：纯逻辑断言（零游戏 DLL、零网络）
node tools/check-copy.mjs                         # 文案门禁：三份词条表键集一致 + 前端零硬编码文案
node tools/check-css.mjs                          # CSS 门禁：Cohtml 吃不下的写法一律拦下（R1~R7，见文件头）
node tools/dev-preview.mjs &                       # 预览台（默认 8765，被占会顺延并把实际地址打在日志里）
node tools/verify-ui.mjs                          # 交互后状态的 DOM 断言（下拉互斥、上传四态、词条没漏…）
node tools/shot.mjs                               # 同一批状态实拍成 PNG，给人眼复核布局
```
数据面（改契约时要跑）：
```bash
cd ../blueprinthub-workshop && npm ci && npm run check     # 校验；npm run build 重建 catalog
```

## 面板里的数据从哪来（0.3.0 起没有内置模板）
面板**只显示工坊里的真蓝图**。工坊是空库时面板就是空态，这是正常状态，不是坏了 —— 测试蓝图由作者自己在游戏里对着市辖区点「上传蓝图」导出（0.4.0 起这条通了：草稿箱 → `scripts/ingest-draft.mjs` → 工坊）。
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
Bpc/         纯逻辑：JSON、分类/面积档/排序/相关度/镜像轮转/失败分类/质心/相对高程/ID/词条键 + 采集模型与 BPC1 编码/元数据/封面 —— 一个游戏类型都不碰，可离线测
Workshop/    浏览态（拉 index → 拉分页 → 本地筛选排序分页 → 封面预热 → 本机去重计数）+ 采集（DistrictCapture）+ 出草稿（UploadService）
Platform/    数据面客户端（复用 HttpClient、12s 超时、三镜像并发探路、哈希复核）、本地目录口径、本机身份、账号态（只读官方登录，不存任何凭据）
Systems/UI/  Cohtml 桥：一条 GetState（C# 是唯一真值源）+ 一条 Cmd（前端只回报动作）；市辖区面板条目（InfoSectionBase）
UI/          React + webpack 面板源码（入口方块 = 原版 FloatingButton + 随包 images/*.svg 图案）
Locale.cs    全部玩家可见文案：选项页 + 面板 82 条 × zh-HANS / zh-HANT / en-US，其余 9 个官方语言回退 en
tools/       check-copy · check-css · dev-preview 预览台 · verify-ui DOM 断言 · shot 实拍 · seed-dev-catalog 开发覆盖
scripts/     ingest-draft.mjs 草稿投递 · pull-live.mjs 线上核对 · publish.mjs 推模组
tests/t3/    离线回归壳：与 Bpc/ 共用同一份源码，不写桩不复制；`-- --draft <目录>` 用真编码链造一张草稿
(同级仓库) ../blueprinthub-workshop/   公共数据面：schema、catalog 生成器、容量与限速实据
research/    反编译与取证产物（不进 git、不编进产物）
```

## 左上角那颗入口方块为什么长这样（0.3.1 重写）
0.3.0 的实机反馈是两条：「只有白底」和「和 Road Builder / Write Everywhere 叠在一起」。两条同一个根因 —— 我没按游戏自己的做法来：

1. **位置本来就是自动排的，不需要模组算。** `GameTopLeft` 这个槽位背后的容器是
   `.info-menu-layout_E8i{position:absolute;top:10rem;left:10rem;display:flex}`，并且
   `.info-menu-layout_E8i>*{margin:0 6rem 6rem 0}` —— 游戏自己那颗信息面板方块和所有模组的方块都是这一行的 flex 子项，
   间距由这条 margin 给。已上架的 Road Builder / Write Everywhere 里**没有任何一个**算过 left/offset（15 个用 GameTopLeft 的模组 bundle 全扫过）。
   我这边会叠，是因为方块里塞了一个 `width:100%;height:100%` 的涂色 `<span>`，而 flex 子项默认可伸可缩 —— 撑破以后就压到邻居身上。
   现在：外层 `.bph-toggle` 只负责 `position:relative;flex:0 0 auto`（占位、不伸不缩），尺寸由官方 `.button_ke4` 决定。
2. **图案交给官方组件画，路径用真文件。** 官方 `Button` 的 `variant="floating"` 映射到 theme `{button:"button_ke4",icon:"icon_be5"}`，
   且有 `src` 时它自己渲染 `<img class=icon_be5 onError=缺图占位>`（`tinted` 才上 mask）。
   0.3.0 用的是 `mask-image:url("data:image/svg+xml,...")` + `background-color:#fff` 的空 span：
   Cohtml 不吃 data URI 的 mask（官方整份 index.css / index.js 里 data URI **0 次**，mask 全是 `url(Media/...svg)` 文件路径），
   mask 一失效，剩下的就是一整块实心白方 —— 玩家看到的「白底」就是这个。
   现在图案是随包发布的 `UI/images/BlueprintHub_Glyph.svg`（白色描边的区划图），引用写 `coui://ui-mods/images/...`；
   模组包根就是 `ui-mods` 宿主（FACT：`ModManager.InitializeUIModules` 对每个模组 `AddHostLocation("ui-mods", 资产目录)`）。
   csproj 会硬检查这个 svg 在不在，`scripts/publish.mjs` 会把 `images/` 一起打进内容包（漏了它 = 线上版本只剩蓝底）。
3. **面板"丑"是同一条规则的连带结果**：`border:1rem solid var(--x,兜底)`、`background:var(--x,兜底)` 这类
   「shorthand + var()」Cohtml 直接整条丢弃，所以 0.3.0 的卡片描边、类别选中底色、当前页码高亮**根本没画出来**。
   这条连同其它几条钉成了 `tools/check-css.mjs`，规则出处写在文件头。

## 闪退怎么归因（别猜，看 dump）
`%LOCALLOW%\Colossal Order\Cities Skylines II\.cache\backtrace\crashpad\reports\*.dmp` 是游戏的 crashpad 落盘处，
一个 dmp 一次事件（`metadata` 里还带着上传回执）。minidump 可以自己解析：
stream 6 = 异常记录（`code` 在 +8、`ExceptionAddress` 在 +24、参数从 +32 起），stream 4 = 模块表（本机实测 stride 108，
`base` 在 +0、`size` 在 +8、`nameRVA` 在 +20，UTF-16 串），拿地址去模块区间里查就得到「崩在哪个 dll + 偏移」。
`0xc0000005` 是真访问违例；`0x0517a7ed` 是 CLR 的托管异常 / ANR 上报（`ANRException: Blocked thread detected.` = 主线程卡住被看门狗记一笔），
不是同一种事。2026-09-28 那六条记录里三条真崩溃全部落在游戏自己的 `Cities2_Data\Plugins\x86_64\lib_burst_generated.dll + 0x164e3ea`。

## 文案为什么绕一大圈（需求 4 的架构决定）
桥层 JSON 里**没有任何成品句子**，只有词条 slug 与数字；句子在游戏本地化词典里（`Locale.BuildPanelMap` 注册 12 个语言），
前端用 `cs2/l10n` 的 `useLocalization().translate(key, fallback)` 取词。三个理由：
1. 键形如 `Options.BLUEPRINT_HUB.<SLUG>[BlueprintHub.BlueprintHub.BlueprintHubMod.Panel.<SLUG>]`，
   BridgeTheLanguageGap 会把 identifier 命中模组 setting.id 的词条归入「模组选项及说明」→ 玩家侧十种语言由它机翻，我们不必自己交。
2. 前端硬编码中文 = 翻不动（TrafficToolEssentials 的面板就是这个毛病），所以 `tools/check-copy.mjs` 直接把这条钉成门禁。
3. `useLocalization()` 返回的是 `{translate, unitSettings}` 而**不是**扁平词典（游戏 UI bundle 里 `dc()` 的实现）——
   按 `bag[key]` 取值取不到东西，这个坑在兄弟模组的旧写法里踩过一次。

## 兼容性
零 Harmony：不与任何模组抢补丁、不引入共享 `0Harmony.dll` 的版本抢占问题。0.4.0 把市辖区面板那颗上传按钮也做成了零侵入（继承 `InfoSectionBase` + `AddBottomSection`，见下面的 FACT 行），所以整支模组仍然没有 Harmony 依赖 —— 游戏大版本改了内部实现，最坏结果是那颗按钮不出现，而主面板会显示兜底入口（`hooks.districtSection` 报假阳性这件事已被 `cmd("hookok")` 堵掉）。

## 已定死的机器事实（细节别靠记忆）
| 结论 | 证据 |
|---|---|
| `UISystemBase.AddUpdateBinding` 存在，`OnUpdate` 主线程逐个 `Update()` | `ilspycmd Game.dll -t Game.UI.UISystemBase` |
| `GetterValueBinding.Update()` 只在视图 active 时调 getter，值不变不推 JS | `research/UIBinding/Colossal.UI.Binding.decompiled.cs:2291` |
| `CallBinding` 走 `view.BindCall`，`TriggerBinding` 走 `RegisterForEvent` | 同文件 :639 / :1997 |
| `AddHostLocation(host, path, shouldWatch=true, priority=0)` | `ilspycmd Colossal.UI.dll -t Colossal.UI.UISystem` :259 |
| 前端可用槽位：Menu / Editor / Game / GameTopLeft / GameTopRight / GameBottomRight / UniversalModMenu（**没有** GameBottomLeft）；`append(target, comp, index?)` 支持第三个索引参数 | 官方模板 `Content/Game/.ModdingToolchain/npx-create-csii-ui-mod/template/types/modding.d.ts` |
| `GameTopLeft` 的容器 = `.info-menu-layout_E8i{position:absolute;top:10rem;left:10rem;display:flex}` + `>*{margin:0 6rem 6rem 0}`：这一排自动排，模组不该自己算 left | `Cities2_Data/Content/Game/UI/index.css`（偏移 ≈380441） |
| `Button` 的合法 variant = flat / primary / round / menu / icon / **floating** / text / default；floating → theme `{button:"button_ke4",icon:"icon_be5"}`；`cs2/ui` 另导出 `FloatingButton`、`Icon`、`Panel`、`PanelSection`、`Portal`、`Scrollable`、`Dropdown` | `index.js` 映射表 @713141 + 官方模板 `types/ui.d.ts` |
| 官方 Button 有 `src` 时自己渲染 `<img class=icon onError=缺图占位>`，`tinted` 才走 TintedIcon（div + `mask-image`） | `index.js` @634027（icon-button.tsx 的 `$b`） |
| Cohtml 2.2.1.3：**shorthand 里不能用 `var()`**（官方 `background:<var>` 仅 1 处，每次开机都被打警告并整条丢弃；`border:<var>` 0 处）；**`var()` 的逗号兜底解析不了**（把 `normalTextColor,#F0FBFF` 整串当变量名）；不认 `object-fit` / `outline` / `word-wrap` / `text-rendering` / `:focus-within` / `:first-of-type`；mask 只吃能加载的资源 URL（官方 data URI 0 次、`-webkit-mask*` 0 次、`mask-image` 26 次） | 官方 index.css 全文统计 + 本机 `Logs/UI.log` 实机警告 |
| 官方 token 里**没有** `--normalBorderColor`（0.3.0 用了它 → 边框一条都不生效）；有 `--stroke1/2`、`--gap2`（px 刻度）、`--selectedColor:#1e83aa`、`--accentColorNormal` 三套主题值（蓝 / 深蓝 / 橙） | 同一份 index.css 逐条 grep |
| `RegisterInOptionsUI` 只在世界为 null 时失败，且对模组是 `void`（静默） | `decompiled/Game/Settings/Setting.cs:179`、`Game/Modding/ModSetting.cs:46` |
| `PlatformManager.instance.userSpecificPath` 平台未就绪时为 null | `research/api-PlatformManager.txt:200` |
| `Colossal.UI.Binding` 里也有 `JsonWriter`（与自写的那个撞名） | 编译期 CS0104 实录 |
| 游戏 Managed 里**没有** `Colossal.Json.dll`，只有 `Newtonsoft.Json.dll` | 列目录实测 |
| FloatSlider：`getter = property × scalarMultiplier`、`setter = property = ui ÷ scalarMultiplier`，而 `min/max/step` 原样交给界面 | `decompiled/Game/UI/Menu/AutomaticSettings.cs:1333` |
| 原版强调色 `--accentColorNormal:#4bc3f1`、文本 `--normalTextColor:#F0FBFF`、正/警/负 `#8bdb46 / #ffa42d / #e95f4a` | `Cities2_Data/Content/Game/UI/index.css` |
| `useLocalization()` → `{translate(key,fallback), unitSettings}`，不是扁平词典 | 游戏 UI bundle `index.js` 里 `dc()`（偏移 ≈433500） |
| 官方术语：District=市辖区、Zone=功能区、Prop=设施、Industrial=工业、Map Tile=区块、Sort by=排序方式、UI Transparency=UI透明度 | `ToolModeMemory/research/locale/zh_en.json`（官方语言包导出） |
| 往市辖区面板塞条目 = 继承 `Game.UI.InGame.InfoSectionBase` + `SelectedInfoUISystem.AddBottomSection(...)`（:255，底部那排就是删除键所在的 footer），**不需要 Harmony**；`InfoSectionBase.Write()` 用 `writer.TypeBegin(GetType().FullName)` 交出类型名 ⇒ 前端必须用同一个全名当键：`moduleRegistry.extend("game-ui/game/components/selected-info-panel/selected-info-sections/selected-info-sections.tsx", "selectedInfoSectionComponents", m => ({...m, "<C# 类全名>": Comp}))` | `research/dumps/Game_UI_InGame_InfoSectionBase.cs:123-140`、`Game_UI_InGame_SelectedInfoUISystem.cs:111/255`；先例：已上架的 AdvancedBuildingControl（它的 `.mjs` 里就是 `map["AdvancedBuildingControl.Systems.SIP_ABC"]`） |
| 运行时槽位有 **8** 个（含 `GameBottomLeft`）：Menu / Editor / Game / GameTopLeft / GameTopRight / GameBottomLeft / GameBottomRight / UniversalModMenu —— 官方模板的 `modding.d.ts` 少写了一个，以 bundle 为准 | 游戏 UI bundle 里 `ModdingHook` 注册表逐个数（0.4.0 复核） |
| `1u = 8m × 8m = 64 ㎡`（一个可划分单元格）；`1 地图区块 = 14336/23 = 623.3043478m` 见方 = `388508.31 ㎡ ≈ 6070.44u`（不是整数，所以界面一律写「≈」） | `ZoneUtils.CELL_SIZE=8f / CELL_AREA=64f`、`Game.Areas.MapTileSystem.LEGACY_CELL_SIZE=623.3043f`、`MapTilePurchaseSystem.kMapTileSizeModifier=1/623.3043478²` |
| 地形高度图是 `4096²` 采样铺满 `14336m` ⇒ 一个采样点 ≈ `3.5m`，**不等于 1u**；读法 `TerrainSystem.GetHeightData(true)` + `TerrainUtils.SampleHeight` | `research/dumps/`（TerrainHeightData / TerrainSystem） |
| 采集没有 manager：区域边界 = `DynamicBuffer<Game.Areas.Node>`，四类四叉树 = `Game.{Net,Objects,Zones,Areas}.SearchSystem.Get*SearchTree()`，用法照 ZoneSnapper 的 `WorldSampler.cs`（`deps.Complete()` → `AddNetSearchTreeReader` → `tree.Iterate(ref gather)`），迭代器必须同时实现 `INativeQuadTreeIterator` 与 `IUnsafeQuadTreeIterator` | `research/dumps/`、ZoneSnapper 源码 |
| 玩家的 SteamID64 **拿不到**：PSI 只暴露 `Social.DisplayName`。所以 `authorId = sha256(userSpecificPath / 设备号)` 前 16 位十六进制，`bpId = b<GUID>-<authorId>` —— 不把账号号编进公开仓库每张 meta.json | `research/PdxSdkPlatform.cs`、`Bpc/BlueprintId.cs` |

## 上传这条链怎么跑（0.4.0）
游戏里点「生成蓝图」到「工坊里能看到」一共四步，**跨两台机器、一个公共仓库**，中间没有任何凭据：

1. **采集**（`Workshop/DistrictCapture.cs`）：选中市辖区 → 读边界环、四类四叉树、地形相对高差、资产表；每类都有条数上限（`CaptureModel.MAX_*`），超了直接失败，并把「哪一类、多少、上限多少」交给词条 `DUPLOAD_TOO_BIG`（上限是**条目数**，不是面积）。
2. **打包**（`Bpc/BpcCodec.cs` + `MetaWriter.cs` + `CoverSvg.cs`）：五节各自编码成 `BPC1` 定长记录块，按 sha256 前 16 位内容寻址；`meta.json` 只写 schema 要求的字段，封面是一张自绘 SVG。
3. **落草稿**（`Workshop/UploadService.cs`）：写到 `ModsData/BlueprintHub/drafts/<bpId>/`，**到此为止** —— 模组不持有任何能写公共仓库的凭据（这是有意的：客户端里塞可写 token = 谁的仓库都能推）。
4. **投递**（`scripts/ingest-draft.mjs`）：作者本机把草稿搬进工坊仓库（验 bpId/authorId 形状、逐节复算哈希、拒绝草稿目录里的陌生文件、同 ID 不同内容硬失败），再跑工坊的 `tools/build-catalog.mjs` 重建索引，最后由人 `git push`。

**临时文件**（作者点名要自动清理）：载入时 `LocalLibrary.PurgeTemp()` 清整个 TempDir；面板关闭时再清一次（正在预览的封面用 pinned 名单保住）；封面缓存限量 240 个 / 160 MB，超了按最后访问时间淘汰；草稿箱只留最近 12 张；写入先落 `.tmp` 再改名，异常路径上 `.tmp` 必删；登出与卸载删掉头像缓存文件。上一版遗留的旧命名草稿/封面（bpId 换代之前的形状）也会被这一轮清理带走。

## 本地怎么测（没有游戏也要测）
| 闸门 | 命令 | 钉住什么 |
|---|---|---|
| 读侧算术 | `dotnet run --project tests/t3 -c Release` | 解析 / 排序 / 分页 / 量化 / 桥层键名，277 条断言；同一份 `Bpc/*.cs` 两边编，不写桩 |
| 端到端投递 | `dotnet run --project tests/t3 -c Release -- --draft <目录>` → `node scripts/ingest-draft.mjs <该目录>` | 用**真的**编码链造一张草稿，再走 ingest + 工坊 builder：格式一改这条就会红 |
| 文案 | `node tools/check-copy.mjs` | 三份词条表键集一致、没有裸 slug 上界面、没有硬编码句子、占位符白名单 |
| CSS | `node tools/check-css.mjs` | Cohtml 吃不下的一切写法（shorthand 里的 `var()`、逗号兜底、data URI、未支持属性、token 白名单） |
| 交互后状态 | `node tools/dev-preview.mjs &` 然后 `node tools/verify-ui.mjs` | 25 条 DOM 断言：下拉互斥、点外面收起、选中类型的定义、u 面积、上传四态、词条没漏 |
| 实拍 | `node tools/shot.mjs` | 同一批状态出图（`?act=` 驱动页面自己点），给人眼复核布局 |
| 有目录可读 | `node tools/seed-dev-catalog.mjs --yes-dev-data` | 假蓝图喂进**真的**工坊 builder 再复制成 dev-catalog，杜绝「开发机能跑、线上不能跑」 |

这套全绿只说明**前端、桥层与数据面自洽**。ECS 采集、`InfoSectionBase` 注入、Cohtml 的字体与 rem 基准仍然只能实机验 —— 这三处是本仓库已知的、机器测不到的部分。

## 当前状态
**0.4.0**（M0 + M1 + M3 的采集导出段完成）：在 0.3.1 的只读浏览之上，这一版按作者点名的九条改完并本地测过：
透明度默认 90%；**市辖区面板底部出现「上传蓝图」**（零 Harmony 的 `InfoSectionBase` 注入 + 前端 `moduleRegistry.extend`），
采集 → 打包 → 本机草稿箱这条链跑通（`scripts/ingest-draft.mjs` 补上最后一米）；右上角改成**账号按钮**（未登录「登录」/登录后头像）
并在左边挂一句提示；面积一律按 **u** 讲（三档重划 + 换算提示）；排序扩到 **8 项**（新增 createdAt 契约字段）；
社区类型改成 **全部 + 7 类**（产业区 / 文教区 / 交通枢纽区 / 公共服务区，去掉混合区），选中类型的定义说明挪到左栏清单下方；
两个筛选下拉互斥且点外面即收起。安全与稳定性按作者要求过了一遍（见「上传这条链怎么跑」末尾）。

M2/M4 待做：蓝图详情与**套用**（spawn 道路是最难的一段）、草稿的一键投递（现在要作者本机跑脚本）、账号页。

**上传仍按作者指示暂缓**：`ModPublisher Publish` 回 "You have too many private mods, max 3." —— 账号已有 3 个 Private 模组（公开 API 列不出 Private 条目，只列到 3 个 public）。内容包与命令都备好了（`node scripts/pull-live.mjs` 先核对线上 → `node scripts/publish.mjs`），等作者决定腾哪个名额；不擅自改 Public 绕过。
桥接口形状已冻结（`GetState` + `Cmd`），M2/M3 只在 JSON 里加字段、在 `OnCmd` 里加分支，不加新绑定。
