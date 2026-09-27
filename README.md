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
| 面板透明度 | 50% | 20%~100%，5% 一档 |
| 面板快捷键 | 未绑定 | 需求里要求默认空，由玩家自己设 |
| 下载中退出要确认 | 开 | 外部 I/O 有 12 s 硬超时，仍给一次确认 |
| 关于 | — | 版本 / 作者 yuexian / Ko-fi / 论坛 / RAINBOW 官网 |

## 构建
```bash
dotnet build -c Release          # 0 错误；后处理会补三平台 Burst 桩并部署
```
数据面（改契约时要跑）：
```bash
cd workshop && npm ci && npm run check     # 校验；npm run build 重建 catalog
```

## 目录
```
Bpc/         纯逻辑：分类/面积档/排序/相关度/镜像轮转/失败分类/质心/相对高程/ID —— 不碰游戏类型，可离线测
Platform/    数据面客户端（复用 HttpClient、12s 超时、哈希复核）、本地库目录口径
Systems/     采集与套用（M3/M4）
UI/          React + webpack 面板（M1）
workshop/    公共数据面：schema、catalog 生成器、容量与限速实据
research/    反编译与取证产物（不进 git、不编进产物）
```

## 兼容性
零 Harmony：不与任何模组抢补丁、不引入共享 `0Harmony.dll` 的版本抢占问题。唯一可能引入 Harmony 的地方是市辖区面板那颗上传按钮（M3），届时按既有模组的做法带 2.3.3 并单独说明影响范围。

## 当前状态
源码 = v0.1.0 开发中（M0 完成：工程骨架 + 选项页 + 纯逻辑层 + 只读客户端，构建通过、已部署）。未发布到 Paradox Mods。
适配游戏版本 1.6.x（本机 1.6.2f1）。
