# Blueprint Hub（蓝图工坊）· 都市天际线2 模组

把一块**市辖区级别的布局**（道路、地形、建筑、摆件、区划）导出成「蓝图」分享到工坊，也能把别人的蓝图套进自己的地图。

**工作原理（一句话版）**：蓝图不是存档、不是资产包，而是**配方** —— 上传时把区域内的实体裁成五节自定义二进制（terrain / nets / objects / zones / areas）加一份 `meta.json` 清单；套用时按中心点与相对高程在目标地图上**造出普通游戏实体**，之后那张地图归游戏自己管。

- 工坊数据面（公开只读）：<https://github.com/yuexian7/blueprinthub-workshop>
- 读侧地址：`raw.githubusercontent.com` → GitHub Pages → jsDelivr（三镜像，内容寻址可互校）

## 安装
1. Paradox Mods 订阅（发布后）；或
2. 本地：`dotnet build -c Release` 后自动部署到 `%LOCALLOW%\Colossal Order\Cities Skylines II\Mods\BlueprintHub\`

## 设置项（游戏选项 → Blueprint Hub）
| 项 | 默认 | 说明 |
|---|---|---|
| 面板背景不透明度 | 50% | 20%~100%，5% 一档（滑条值 ×100 显示） |
| 打开 / 关闭面板快捷键 | 未绑定 | 需求里要求默认空，由玩家自己设；左上角也常驻按钮 |
| 关于 | — | 版本 / 作者 yuexian / Ko-fi / 论坛 / RAINBOW 官网 |

## 构建
```bash
npm --prefix UI ci && npm --prefix UI run build   # 前端：UI/dist/BlueprintHub.mjs（改 UI/src 后要重跑）
dotnet build -c Release                           # 模组：0 错误；后处理补三平台桩，并把 .mjs + mod.json 一起部署
dotnet run --project tests/t3 -c Release          # 离线 T3：纯逻辑断言（零游戏 DLL、零网络）
```
数据面（改契约时要跑）：
```bash
cd ../blueprinthub-workshop && npm ci && npm run check     # 校验；npm run build 重建 catalog
```

## 本地怎么看面板（工坊还是空库的时候）
```bash
node tools/seed-dev-catalog.mjs            # 往 workshop 里灌假蓝图 → 跑真 builder → 产物落到本机 dev 覆盖目录
node tools/seed-dev-catalog.mjs --items 40 # 想要更多页就加量
node tools/seed-dev-catalog.mjs --clean    # 删掉覆盖，回到真实三镜像
```
覆盖目录 = `%LOCALLOW%\Colossal Order\Cities Skylines II\ModsData\BlueprintHub\dev-catalog\`，
只有本机有该目录时生效，玩家机器上不存在 → 不影响任何真实行为。日志里会明确播报一次「开发覆盖生效」。

## 目录
```
Bpc/         纯逻辑：JSON、分类/面积档/排序/相关度/镜像轮转/失败分类/质心/相对高程/ID —— 一个游戏类型都不碰，可离线测
Workshop/    浏览态（拉 index → 拉分页 → 本地筛选排序分页 → 封面预热 → 本机去重计数）
Platform/    数据面客户端（复用 HttpClient、12s 超时、三镜像、哈希复核）、本地目录口径、本机身份
Systems/UI/  Cohtml 桥：一条 GetState（C# 是唯一真值源）+ 一条 Cmd（前端只回报动作）
UI/          React + webpack 面板源码
Locale.cs    选项页词条（zh-Hans / zh-HANT 中文，其余回退 en；M5 补 12 语言与面板文案）
tools/       seed-dev-catalog.mjs 等开发工具
tests/t3/    离线回归壳：与 Bpc/ 共用同一份源码，不写桩不复制
(同级仓库) ../blueprinthub-workshop/   公共数据面：schema、catalog 生成器、容量与限速实据
research/    反编译与取证产物（不进 git、不编进产物）
```

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

## 当前状态
M0 + **M1 完成**：工程骨架、选项页、只读数据面客户端、Cohtml 面板壳与真实浏览（7 类社区、搜索、面积档、5 种排序、3×5 分页、点赞/套用本机去重、封面 coui、加载中/空/无结果/失败四态）。
M2 待做：蓝图详情、资产清单与一键订阅、套用（预览放置）。M3 待做：采集导出 + 上传面板 + 草稿箱 + 账号页。
未发布到 Paradox Mods。本机：`dotnet build -c Release` 0 错误 0 警告，T3 170 通过。
桥接口形状已冻结（`GetState` + `Cmd`），M2/M3 只在 JSON 里加字段、在 `OnCmd` 里加分支，不加新绑定。
