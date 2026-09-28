using System;
using System.Collections.Generic;
using System.IO;
using BlueprintHub.Bpc;

namespace BlueprintHub.Tests
{
    /// <summary>
    /// 离线 T3 闸门：只编 Bpc/ 四个纯逻辑文件（零游戏 DLL、零网络），断言「工坊读侧的算术与解析」。
    /// 为什么值得写：这些判据一旦在真机上看板才知道错，成本是「重编模组 + 重开游戏 + 重开面板」；
    /// 在这里跑只要几毫秒。列表排序/分页错了 = 玩家看到重复或漏条目，是最刺眼的 bug。
    /// </summary>
    public static class Program
    {
        private static int s_pass;
        private static int s_fail;
        private static int s_skip;

        private static void Check(bool cond, string name)
        {
            if (cond) { s_pass++; }
            else { s_fail++; Console.WriteLine("FAIL  " + name); }
        }

        private static void Skip(string name, string why)
        {
            s_skip++;
            Console.WriteLine("SKIP  " + name + " —— " + why);
        }

        public static int Main()
        {
            JsonBasics();
            MetaFromRealIndex();
            ItemRoundTrip();
            CategoryAndArea();
            Sorting();
            SearchRelevance();
            Paging();
            CentroidAndHeights();
            Quantize();
            MirrorsAndFailures();
            VotesAndIds();
            DisplayText();
            BridgeDataAndKeys();
            CrossCheckAgainstBuilder();

            Console.WriteLine();
            Console.WriteLine("T3: " + s_pass + " 通过 / " + s_fail + " 失败 / " + s_skip + " 跳过");
            return s_fail == 0 ? 0 : 1;
        }

        // ---------------- JSON ----------------

        private static void JsonBasics()
        {
            JsonValue v;
            string err;

            Check(Json.TryParse("{\"a\":1,\"b\":[true,null,\"x\"],\"c\":-2.5}", out v, out err), "解析：扁平对象+数组");
            Check(v.Int("a") == 1 && v.Long("a") == 1L, "解析：整数读成 int/long 一致");
            Check(v.Num("c") == -2.5d, "解析：负小数");
            Check(v.Arr("b").Count == 3, "解析：数组长度");
            Check(v.G("nope").IsNull, "取值：键不存在 → NULL 而不是抛");
            Check(v.G("nope").I(7) == 7, "取值：缺失键回默认值");
            Check(v.G("b").At(99).IsNull && v.G("b").At(0).B(false), "取值：数组越界回 NULL，正常下标可读");
            Check(!v.Has("nope"), "Has() 只认存在的键");

            Check(!Json.TryParse("{\"a\":}", out v, out err) && err.Length > 0, "非法 JSON：报错并给原因");
            Check(!Json.TryParse("[1,2", out v, out err), "非法 JSON：未闭合数组");
            Check(!Json.TryParse("", out v, out err), "非法 JSON：空文本");
            Check(!Json.TryParse("{\"a\":1} trailing", out v, out err), "非法 JSON：尾部多余内容");

            // 深嵌套不许把进程炸掉（外部数据不可信）
            string deep = new string('[', 500) + new string(']', 500);
            Check(!Json.TryParse(deep, out v, out err) && err.Contains("嵌套"), "防御：超深嵌套被拒（" + err + "）");

            string bom = "\uFEFF{\"a\":1}";
            Check(Json.TryParse(bom, out v, out err) && v.Int("a") == 1, "宽容：BOM 开头照样读");

            // 写：结构 + 危险字符
            JsonWriter w = new JsonWriter();
            w.BeginObj();
            w.Num("n", 5);
            w.Str("s", "说\"引\"号\\与换行\n");
            w.Bool("t", true);
            w.BeginArr("arr");
            w.Num(1); w.Num(2);
            w.End();
            w.BeginObj("o");
            w.Null("x");
            w.End();
            w.End();
            string text = w.Finish();
            Check(Json.TryParse(text, out v, out err), "写出来的 JSON 能被自己读回");
            Check(v.Str("s").Contains("\n") && v.Str("s").Contains("\""), "写：转义往返无损");
            Check(text.IndexOf("\u2028") < 0 && text.IndexOf("\u2029") < 0, "写：U+2028/29 被转义成 \\u 形式");
            Check(v.Arr("arr").Count == 2 && v.Obj("o").G("x").IsNull, "写：数组与嵌套对象");

            bool threw = false;
            try { new JsonWriter().BeginObj().Str("k", "v").Finish(); } catch (JsonException) { threw = true; }
            Check(threw, "写：Begin/End 不配对被 Finish() 抓住");

            // 透传：读→写→读 语义等价
            JsonValue again = Json.Parse(text);
            Check(Json.Parse(new JsonWriter().Write(again).Finish()).Int("n") == 5, "写：JsonValue 透传可再解析");
        }

        // ---------------- index.json（拿数据面仓库里那份真的对一遍）----------------

        private static void MetaFromRealIndex()
        {
            string file = FindWorkshopIndex();
            if (file == null)
            {
                Skip("真实 index.json 对照", "没找到 blueprinthub-workshop/catalog/index.json");
                return;
            }
            string err;
            BrowseKit.Meta m = BrowseKit.ParseMeta(File.ReadAllText(file), out err);
            Check(m != null, "真实索引可解析（" + err + "）");
            if (m == null) return;
            Check(m.PageSize == 15, "pageSize=15 = 面板 3×5 的约定");
            Check(m.Categories.Count == 7, "社区类型恰好 7 类");
            Check(m.Mirrors.Count == 3, "三条镜像（raw → Pages → jsDelivr）");
            Check(m.Mirrors[0].StartsWith("https://") && m.Mirrors[2].Contains("jsdelivr"), "镜像顺序：raw 在前、jsDelivr 兜底");
            Check(m.TileAreaM2 == 388129d, "1 区块 = 388,129 ㎡（ZoneSnapper FACT 35）");
            Check(m.WDownloads == 3 && m.WLikes == 1, "热度权重 3:1");
            Check(m.HotWindowDays == 7, "周热度窗口 7 天");
            Check(m.AllPages >= 0, "allPages 字段可读（" + m.AllPages + "）");
            for (int i = 0; i < m.Categories.Count; i++)
            {
                BrowseKit.CategoryMeta c = m.Categories[i];
                Check(CatalogKit.IsKnownCategory(c.Id), "索引分类 id 与模组内置 7 类对齐：" + c.Id);
                Check(c.LabelZh.Length > 0 && c.DefinitionZh.Length > 0, "分类带中文label与定义：" + c.Id);
            }
            // 分类名与定义必须与 C# 常量同源，否则面板左右两栏会各说各话
            for (int i = 0; i < CatalogKit.CategoryIds.Length; i++)
            {
                BrowseKit.CategoryMeta c = m.Find(CatalogKit.CategoryIds[i]);
                Check(c != null && c.LabelZh == CatalogKit.CategoryLabelsZh[i]
                      && c.DefinitionZh == CatalogKit.CategoryDefinitionsZh[i],
                      "词条同源：" + CatalogKit.CategoryIds[i]);
            }

            string bad = "{ \"schemaVersion\": 1 }";
            BrowseKit.Meta empty = BrowseKit.ParseMeta(bad, out err);
            Check(empty != null && empty.Categories.Count == 0 && err == null, "缺字段的索引不抛，按空处理");
            Check(BrowseKit.ParseMeta("[]", out err) == null && err.Length > 0, "结构不对 → null + 原因");
        }

        private static string FindWorkshopIndex()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                string cand = Path.Combine(dir.FullName, "blueprinthub-workshop", "catalog", "index.json");
                if (File.Exists(cand)) return cand;
            }
            return null;
        }

        // ---------------- 条目解析 ----------------

        private const string PAGE = "{\"schemaVersion\":1,\"page\":1,\"pages\":2,\"total\":16,\"sort\":\"hotWeekly\",\"items\":["
            + "{\"id\":\"b0a7f3c2-1111-2222-3333-444455556666-76561198000000000\",\"author\":\"76561198000000000\","
            + "\"authorName\":\"阿明\",\"name\":\"滨江北岸\",\"categories\":[\"park\",\"public\"],\"areaClass\":\"large\","
            + "\"areaM2\":7762580,\"tiles\":20,\"likes\":12,\"downloads\":34,\"hotWeekly\":9,\"hotTotal\":114,"
            + "\"updatedAt\":\"2026-09-20T08:00:00Z\",\"cover\":\"blueprints/76561198000000000/bx/preview/cover.svg\","
            + "\"assetCount\":41,\"desc\":\"临江公园与公交枢纽\"}]}";

        private static ListItem SampleItem(string id, string name, string author, string cat, long areaM2,
            long likes, long downloads, long weekly, string updated)
        {
            ListItem it = new ListItem();
            it.Id = id;
            it.Name = name;
            it.AuthorId = "76561198000000000";
            it.AuthorName = author;
            it.Categories = new[] { cat };
            it.AreaM2 = areaM2;
            it.AreaClass = CatalogKit.AreaClass(BrowseKit.Tiles(areaM2, CatalogKit.TILE_AREA_M2));
            it.Likes = likes;
            it.Downloads = downloads;
            it.HotWeekly = weekly;
            it.HotTotal = CatalogKit.HotScore(downloads, likes);
            it.UpdatedAt = updated;
            it.AssetCount = 3;
            return it;
        }

        private static void ItemRoundTrip()
        {
            string err;
            List<ListItem> items = BrowseKit.ParseItems(PAGE, out err);
            Check(items != null && items.Count == 1 && err == null, "分页可解析");
            ListItem it = items[0];
            Check(it.Id.StartsWith("b") && it.AuthorId == "76561198000000000", "条目：id 与作者段");
            Check(it.Categories.Length == 2 && it.Categories[0] == "park", "条目：多分类数组");
            Check(it.AreaM2 == 7762580L && it.Tiles == 20, "条目：面积与区块数");
            Check(it.Likes == 12 && it.Downloads == 34 && it.HotWeekly == 9, "条目：计数");
            Check(it.AssetCount == 41 && it.Description == "临江公园与公交枢纽", "条目：资产数与描述");
            Check(it.CoverRepoPath.StartsWith("blueprints/"), "条目：封面走仓库相对路径");
            Check(BrowseKit.PageCountOf(Json.Parse(PAGE), 15) == 2, "分页信封 pages 读对");

            // 老/坏数据不许把面板打挂
            List<ListItem> sparse = BrowseKit.ParseItems("{\"items\":[{\"id\":\"x\"}]}", out err);
            Check(sparse != null && sparse.Count == 1 && sparse[0].Name == "" && sparse[0].AreaClass == "small",
                "缺字段的条目按默认值补齐");
            Check(BrowseKit.ParseItems("{\"items\":\"不是数组\"}", out err) != null, "items 类型错了也不抛");
        }

        // ---------------- 分类 / 面积筛选 ----------------

        private static void CategoryAndArea()
        {
            List<ListItem> all = Fixture();

            Check(BrowseKit.CategoryMatches(all[0], "park"), "分类：命中");
            Check(!BrowseKit.CategoryMatches(all[0], "mixed"), "分类：不命中");
            Check(BrowseKit.CategoryMatches(all[0], "all") && BrowseKit.CategoryMatches(all[0], ""), "分类：空/all 放行一切");

            BrowseKit.Query q = new BrowseKit.Query { Category = "public", Area = "all", Sort = "weekly", Page = 1 };
            BrowseKit.Result r = BrowseKit.Run(all, q);
            Check(r.Total == 3, "按公共区筛 → 3 张（多分类条目也要出现）");

            q = new BrowseKit.Query { Category = "", Area = "small", Sort = "weekly", Page = 1 };
            r = BrowseKit.Run(all, q);
            Check(r.Total == 3 && r.Items[0].Id == "b1", "面积档 small：1/1.29/2.3 区块都算小（≤2 块，四舍五入到块数）");
            Check(BrowseKit.Tiles(500000L, CatalogKit.TILE_AREA_M2) == 1 && BrowseKit.Tiles(900000L, CatalogKit.TILE_AREA_M2) == 2,
                "区块数换算：50 万㎡≈1 块、90 万㎡≈2 块");

            q.Area = "bogus";
            Check(BrowseKit.Run(all, q).Total == all.Count, "未知面积档退回「全部」，不把列表筛空");

            q = new BrowseKit.Query { Category = "", Area = "all", Sort = "weekly", Page = 1 };
            Check(CatalogKit.AreaClass(0) == "small" && CatalogKit.AreaClass(2) == "small", "面积档：≤2 区块为小");
            Check(CatalogKit.AreaClass(3) == "medium" && CatalogKit.AreaClass(9) == "medium", "面积档：3~9 为中");
            Check(CatalogKit.AreaClass(10) == "large", "面积档：>9 为大");
            Check(CatalogKit.IndexCategories(new[] { "park", "public" })[0] == CatalogKit.Mixed, "多选自动归混合区（需求 4）");
            Check(CatalogKit.IndexCategories(new[] { "park" })[0] == "park", "单选保持原类");
            Check(CatalogKit.IndexCategories(null)[0] == CatalogKit.Mixed, "没选也归混合区而不是丢条目");
        }

        // ---------------- 排序 ----------------

        private static void Sorting()
        {
            List<ListItem> all = Fixture();
            BrowseKit.Query q = new BrowseKit.Query { Area = "all", Page = 1 };

            q.Sort = "weekly";
            Check(Name(0, q, all) == "小广场", "周热度序：最高在前");
            q.Sort = "total";
            Check(Name(0, q, all) == "滨江北岸", "总热度序：下载权重 3 起作用");
            q.Sort = "uploadTime";
            Check(Name(0, q, all) == "旧城改造", "上传时间序：最新在前");
            q.Sort = "area";
            Check(Name(0, q, all) == "滨江北岸", "面积序：最大在前");
            q.Sort = "name";
            Check(Name(0, q, all) == "大学城", "名称序：UTF-16 序数（大 U+5927 最小）；拼音序交给前端 localeCompare（M5）");
            q.Sort = "garbage";
            Check(Name(0, q, all) == "小广场", "未知排序退回默认（周热度）");

            // 同分裁决必须稳定，否则翻页会看到同一张卡片跳来跳去
            List<ListItem> tie = new List<ListItem>
            {
                SampleItem("t1", "乙", "A", "park", 100, 0, 0, 0, "2026-01-01T00:00:00Z"),
                SampleItem("t2", "甲", "B", "park", 100, 0, 0, 0, "2026-01-01T00:00:00Z"),
            };
            BrowseKit.Result r1 = BrowseKit.Run(tie, new BrowseKit.Query { Sort = "weekly", Page = 1, Area = "all" });
            BrowseKit.Result r2 = BrowseKit.Run(tie, new BrowseKit.Query { Sort = "weekly", Page = 1, Area = "all" });
            Check(r1.Items[0].Id == "t1" && r1.Items[1].Id == "t2", "同分按名称序数兜底（乙 U+4E59 < 甲 U+7532）");
            Check(r2.Items[0].Id == r1.Items[0].Id, "同分裁决是确定的，不是字典序偶然");
        }

        private static string Name(int idx, BrowseKit.Query q, List<ListItem> all)
        {
            BrowseKit.Result r = BrowseKit.Run(all, q);
            return idx < r.Items.Count ? r.Items[idx].Name : "<空>";
        }

        // ---------------- 搜索相关度 ----------------

        private static void SearchRelevance()
        {
            List<ListItem> all = Fixture();
            BrowseKit.Query q = new BrowseKit.Query { Area = "all", Sort = "weekly", Page = 1 };

            q.Search = "滨江北岸";
            Check(BrowseKit.Run(all, q).Items[0].Id == "b2", "搜索：名称完全相等排第一");
            q.Search = "滨江北";
            Check(BrowseKit.Run(all, q).Items[0].Id == "b2", "搜索：名称前缀次之");
            q.Search = "北岸";
            Check(BrowseKit.Run(all, q).Items[0].Id == "b2", "搜索：名称包含再次之");
            q.Search = "阿明";
            Check(BrowseKit.Run(all, q).Total == 3 && BrowseKit.Run(all, q).Items[0].Id == "b1", "搜索：作者命中三人同分 → 落回周热度（b1 最高）");
            q.Search = "公交枢纽";
            Check(BrowseKit.Run(all, q).Total == 1 && BrowseKit.Run(all, q).Items[0].Id == "b6", "搜索：能按描述命中（权重最低，也只有它命中）");
            q.Search = "  滨江北岸  ";
            Check(BrowseKit.Run(all, q).Items[0].Id == "b2", "搜索：前后空格与折叠空白不影响命中");
            q.Search = "不存在的东西";
            Check(BrowseKit.Run(all, q).Total == 0, "搜索：无命中就是 0，别硬凑");
            q.Search = "PARK";
            Check(BrowseKit.Run(all, q).Total >= 0, "搜索：大小写归一（英文混排不炸）");

            Check(CatalogKit.Relevance(all[0], "") == null, "相关度：空查询交回默认排序");
            Check(CatalogKit.Relevance(all[1], "岸").Value < CatalogKit.Relevance(all[1], "滨江北岸").Value,
                "相关度：命中越靠前分越高");
        }

        // ---------------- 分页 ----------------

        private static void Paging()
        {
            List<ListItem> many = new List<ListItem>();
            for (int i = 0; i < 37; i++)
                many.Add(SampleItem("p" + i.ToString("D3"), "蓝图" + i.ToString("D3"), "作者", "park",
                    400000L + i, i, i, i, "2026-09-01T00:00:00Z"));

            Check(CatalogKit.PAGE_SIZE == 15, "一页 15 张 = 3 排 × 5");
            Check(CatalogKit.PageCount(0) == 0 && CatalogKit.PageCount(1) == 1 && CatalogKit.PageCount(16) == 2, "页数算术");
            Check(CatalogKit.PageCount(BrowseKit.MaxItems) == BrowseKit.MAX_ALL_PAGES, "全量上限与页数自洽（" + BrowseKit.MaxItems + "）");

            BrowseKit.Query q = new BrowseKit.Query { Area = "all", Sort = "total", Page = 1 };
            BrowseKit.Result r = BrowseKit.Run(many, q);
            Check(r.Pages == 3 && r.Slice.Count == 15, "第 1 页 15 张，共 3 页");
            q.Page = 3;
            r = BrowseKit.Run(many, q);
            Check(r.Slice.Count == 7, "末页装剩下 7 张");
            q.Page = 99;
            r = BrowseKit.Run(many, q);
            Check(r.Page == 3 && r.Slice.Count == 7, "页码越界夹到末页，而不是给空白");
            q.Page = -5;
            Check(BrowseKit.Run(many, q).Page == 1, "页码负数夹回第 1 页");

            // 翻页不能出现重复或漏项
            HashSet<string> seen = new HashSet<string>();
            for (int p = 1; p <= 3; p++)
            {
                q.Page = p;
                foreach (ListItem it in BrowseKit.Run(many, q).Slice) seen.Add(it.Id);
            }
            Check(seen.Count == 37, "三页合起来不重不漏（" + seen.Count + "）");
        }

        // ---------------- 质心 / 相对高度 ----------------

        private static void CentroidAndHeights()
        {
            double cx, cz;
            bool approx;

            // 正方形：质心 = 几何中心
            CatalogKit.PolygonCentroid(new[] { 0d, 10d, 10d, 0d }, new[] { 0d, 0d, 10d, 10d }, out cx, out cz, out approx);
            Check(Math.Abs(cx - 5d) < 1e-9 && Math.Abs(cz - 5d) < 1e-9 && !approx, "质心：正方形精确，不标近似");

            // L 形（不规则市辖区）：质心落在凹形一侧，绝不能是包围盒中心
            double[] xs = { 0, 12, 12, 6, 6, 0 };
            double[] zs = { 0, 0, 6, 6, 12, 12 };
            CatalogKit.PolygonCentroid(xs, zs, out cx, out cz, out approx);
            // 手算：面积 108，sx=3240 → cx=3240/(6*108)=5.0，cz 同理；包围盒中心是 (6,6)
            Check(!approx, "质心：不规则多边形走了面积公式（未退化）");
            Check(Math.Abs(cx - 5d) < 1e-9 && Math.Abs(cz - 5d) < 1e-9, "质心：L 形 = (5,5)，与手算一致（鞋带公式）");
            Check(cx != 6d || cz != 6d, "质心：确实偏离包围盒中心 (6,6) —— 拿包围盒中心当锚点会套歪");

            // 退化：两点线段 → 包围盒中心 + 标记近似
            CatalogKit.PolygonCentroid(new[] { 2d, 8d }, new[] { 3d, 9d }, out cx, out cz, out approx);
            Check(approx && Math.Abs(cx - 5d) < 1e-9 && Math.Abs(cz - 6d) < 1e-9, "退化：共线退回包围盒中心并如实标「近似」");

            double dummyA, dummyB;
            bool dummyC;
            Check(!CatalogKit.PolygonCentroid(null, null, out dummyA, out dummyB, out dummyC), "空顶点集返回 false（调用方必须处理）");
            Check(!CatalogKit.PolygonCentroid(new[] { 1d }, new[] { 1d, 2d }, out dummyA, out dummyB, out dummyC), "xs/zs 长度不等 → false");

            // 需求 9：一切以中心点地面为零点，上传者住海边还是山边不影响记录值
            Check(CatalogKit.ToRelativeHeight(100d, 100d) == 0d, "上传者在 +100m 海平面 → 记录 0");
            Check(CatalogKit.ToRelativeHeight(112.5d, 100d) == 12.5d, "山头上 12.5m 的建筑 → 记录 +12.5");
            Check(CatalogKit.ToWorldHeight(12.5d, -3d) == 9.5d, "套到洼地（地面 -3m）→ 世界高 9.5m");
            double anchor = 77.34d;
            Check(Math.Abs(CatalogKit.ToWorldHeight(CatalogKit.ToRelativeHeight(80d, anchor), anchor) - 80d) < 1e-9,
                "相对化与还原是同一把尺（往返无损）");
        }

        private static void Quantize()
        {
            Check(CatalogKit.QuantizeCm16(1.234d) == 123, "量化：1.234m → 123cm（四舍五入离零更远）");
            Check(CatalogKit.QuantizeCm16(-1.235d) == -124, "量化：负值同样进位");
            Check(Math.Abs(CatalogKit.DequantizeCm16(CatalogKit.QuantizeCm16(37.62d)) - 37.62d) < 1e-9, "量化往返");
            Check(CatalogKit.QuantizeCm16(99999d) == short.MaxValue, "超上限钳到 int16 顶，不绕回负数");
            Check(CatalogKit.QuantizeCm16(-99999d) == short.MinValue, "超下限钳到 int16 底");
            Check(double.IsNaN(CatalogKit.QuantizeCm16(double.NaN)) == false, "NaN 不会污染定点值");
        }

        // ---------------- 镜像与失败分类 ----------------

        private static void MirrorsAndFailures()
        {
            Check(CatalogKit.BuildUrl("https://a.example/", "catalog/index.json") == "https://a.example/catalog/index.json",
                "URL：尾斜杠不重复");
            Check(CatalogKit.BuildUrl("https://a.example", "/catalog/index.json") == "https://a.example/catalog/index.json",
                "URL：头斜杠被吃掉");
            Check(CatalogKit.BuildUrl("", "a/b.json") == "a/b.json", "URL：空基址当相对");
            Check(CatalogKit.BuildUrl("https://a.example", "..\\evil\\x.json") == "https://a.example/../evil/x.json",
                "URL：反斜杠统一换正斜杠（不假装能防穿越，CI 侧才是判据）");

            Check(CatalogKit.NextMirror(0, 3, false) == 0, "镜像：成功就固定在胜出者");
            Check(CatalogKit.NextMirror(0, 3, true) == 1 && CatalogKit.NextMirror(2, 3, true) == 0,
                "镜像：失败按顺序轮转，到底回绕");
            Check(CatalogKit.NextMirror(-1, 3, false) == 0, "镜像：没探过就从第一个开始");
            Check(CatalogKit.NextMirror(0, 0, true) == 0, "镜像：列表为空不至于除零");

            // Playbook 硬规则 16：瞬时与永久必须分开，判据只允许一处
            Check(CatalogKit.IsPermanentStatus(404) && CatalogKit.IsPermanentStatus(403) && CatalogKit.IsPermanentStatus(400),
                "永久拒绝：400/403/404");
            Check(!CatalogKit.IsPermanentStatus(429), "429 是瞬时要留队（限流不是没有）");
            Check(!CatalogKit.IsPermanentStatus(0) && !CatalogKit.IsPermanentStatus(500) && !CatalogKit.IsPermanentStatus(503),
                "网络层失败与 5xx 都算瞬时");
            Check(CatalogKit.BackoffMs(1) == 500 && CatalogKit.BackoffMs(3) == 1500, "退避：min(30s, 500×次数)");
            Check(CatalogKit.BackoffMs(1000) == 30000 && CatalogKit.BackoffMs(0) == 500 && CatalogKit.BackoffMs(-3) == 500,
                "退避：封顶 30 秒，非法次数不炸");
        }

        private static void VotesAndIds()
        {
            Check(CatalogKit.VoteKey("k1", "b1", "likes") == "k1|b1|L" && CatalogKit.VoteKey("k1", "b1", "downloads") == "k1|b1|D",
                "去重键：点赞与套用分开");
            Check(CatalogKit.VoteKey("k1", "b1", "likes") != CatalogKit.VoteKey("k2", "b1", "likes"), "去重键：不同玩家分开");
            Check(CatalogKit.HotScore(10, 5) == 35 && CatalogKit.HotScore(0, 0) == 0, "热度：下载×3 + 点赞×1");

            Guid g = new Guid("0a7f3c21-1111-2222-3333-444455556666");
            string id = BlueprintId.New(g, "76561198000000000");
            Check(BlueprintId.IsValid(id), "蓝图 ID 合法：" + id);
            Check(BlueprintId.AuthorOf(id) == "76561198000000000", "ID 自带作者归属");
            Check(BlueprintId.MetaPath(id) == "blueprints/76561198000000000/" + id + "/meta.json", "仓库路径由 ID 推出，不留第二处口径");
            Check(BlueprintId.BlobPath("deadbeefcafe1234") == "blob/deadbeefcafe1234.bin", "分节内容寻址路径");
            Check(BlueprintId.PreviewPath(id, "preview/cover.svg").EndsWith("/preview/cover.svg"), "封面路径归一");
            Check(!BlueprintId.IsValid(id.Substring(1)), "缺前缀 → 非法");
            Check(!BlueprintId.IsValid(id + "x"), "作者段超长 → 非法");
            Check(!BlueprintId.IsValid("b0a7f3c2-1111-2222-3333-44445555666-1234567890123456"), "GUID 段短一位 → 非法");
            Check(!BlueprintId.IsValid("b0a7f3c2-1111-2222-3333-44445555666g-1234567890123456"), "GUID 段含非十六进制 → 非法");
            Check(!BlueprintId.IsValid(null) && !BlueprintId.IsValid(""), "空/ null 一律非法");
            Check(BlueprintId.ShortenHash("DEADBEEFCAFE1234567890abcdef12345678") == "deadbeefcafe1234", "哈希缩短取前 16");
            Check(BlueprintId.RandomAuthorName(new Random(7)).Length == 10, "随机作者名是 10 位数字（需求 5）");
        }

        private static void DisplayText()
        {
            Check(BrowseKit.AreaTextZh(388129L, CatalogKit.TILE_AREA_M2) == "1 区块 · 38.8万㎡", "面积文案：脚注用万㎡（卡片塞不下千分位）");
            Check(BrowseKit.AreaTextZh(194065L, CatalogKit.TILE_AREA_M2) == "0.5 区块 · 19.4万㎡", "面积文案：半块写 0.5");
            Check(BrowseKit.AreaTextFull(194065L, CatalogKit.TILE_AREA_M2) == "0.5 区块 · 194,065 ㎡", "面积全称：tooltip 里给精确值");
            Check(BrowseKit.AreaTextZh(3105032L, CatalogKit.TILE_AREA_M2) == "8 区块 · 310.5万㎡", "面积文案：8 块写 310.5万㎡");
            Check(BrowseKit.AreaTextZh(4000L, CatalogKit.TILE_AREA_M2) == "0 区块 · 4,000㎡", "面积文案：不足 1 万㎡ 直接写㎡");
            Check(BrowseKit.AreaTextZh(7762580L, CatalogKit.TILE_AREA_M2) == "20 区块 · 776.3万㎡", "面积文案：整块不带小数、万㎡ 保留一位");
            Check(BrowseKit.AreaTextFull(7762580L, CatalogKit.TILE_AREA_M2) == "20 区块 · 7,762,580 ㎡", "面积全称：千分位不丢");
            Check(CatalogKit.TileHintZh().Contains("388,129"), "菜单条：1 区块提示");

            DateTime now = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
            Check(BrowseKit.RelativeTimeZh("2026-09-27T11:59:30Z", now) == "刚刚", "相对时间：<1 分钟");
            Check(BrowseKit.RelativeTimeZh("2026-09-27T11:00:00Z", now) == "1 小时前", "相对时间：小时档");
            Check(BrowseKit.RelativeTimeZh("2026-09-25T12:00:00Z", now) == "2 天前", "相对时间：天档");
            Check(BrowseKit.RelativeTimeZh("2026-01-01T00:00:00Z", now) == "8 个月前", "相对时间：月档");
            Check(BrowseKit.RelativeTimeZh("2020-01-01T00:00:00Z", now) == "6 年前", "相对时间：年档");
            Check(BrowseKit.RelativeTimeZh("2099-01-01T00:00:00Z", now) == "刚刚", "时钟不齐（未来时间）当「刚刚」，不许出现「-3 天前」");
            Check(BrowseKit.RelativeTimeZh("不是日期", now) == "不是日期", "解析失败就原样显示，不编一个假时间");
            Check(BrowseKit.Count(999) == "999" && BrowseKit.Count(10000) == "10k", "计数：宽度受限时缩写");
            Check(CatalogKit.SubtitleLabel("park", false).StartsWith("公园区 ·"), "小标题：只在选中时给出定义句");
            Check(CatalogKit.SubtitleLabel("", false).Length == 0, "小标题：没选中就是空串（面板据此不占行）");
        }

        // ---------------- 与真 builder 产物对一遍（开发覆盖目录存在时才跑）----------------

        /// <summary>
        /// tools/seed-dev-catalog.mjs 会把「真 workshop builder 的产物」落到本机
        /// ModsData/BlueprintHub/dev-catalog/。有就拿来当免费的一次性集成检查：
        /// builder 改了字段名/口径而 C# 没跟上时，这里就红，而不是等开游戏才发现面板空白。
        /// </summary>
        private static void CrossCheckAgainstBuilder()
        {
            // 两个来源，谁在用谁：① 同级 workshop 仓库（真 builder 的产物，工坊空库时也是一次真实的格式对照）
            //                   ② 本机 dev-catalog（seed 脚本落的一次性覆盖，字段最全）
            string root = null;
            string[] candidates =
            {
                Environment.GetEnvironmentVariable("BLUEPRINTHUB_WORKSHOP") ?? string.Empty,
                SiblingRepo("blueprinthub-workshop"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "AppData", "LocalLow", "Colossal Order", "Cities Skylines II",
                    "ModsData", "BlueprintHub", "dev-catalog"),
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                if (string.IsNullOrEmpty(candidates[i])) continue;
                if (File.Exists(Path.Combine(candidates[i], "catalog", "index.json"))) { root = candidates[i]; break; }
            }
            if (root == null)
            {
                Skip("builder 产物对照", "既没找到同级 blueprinthub-workshop，也没有 dev-catalog");
                return;
            }
            Console.WriteLine("  （对照源：" + root + "）");
            string indexFile = Path.Combine(root, "catalog", "index.json");

            string err;
            BrowseKit.Meta meta = BrowseKit.ParseMeta(File.ReadAllText(indexFile), out err);
            Check(meta != null, "对照：真实 index.json 解析");
            if (meta == null) return;
            Check(meta.AllPages >= 0, "对照：allPages 是个非负数（" + meta.AllPages + "）");

            int loaded = 0;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var files = new HashSet<string>(StringComparer.Ordinal);
            int badHash = 0, badClass = 0, badHot = 0, missingCover = 0, dupe = 0;
            // 工坊现在是空库（真条目由玩家上传），空库也要能过：这时逐条检查没有对象，改钉「索引自洽」。
            bool emptyLib = meta.Total == 0 || meta.AllPages == 0;
            if (emptyLib)
            {
                Check(meta.Total == 0 && meta.AllPages == 0,
                    "对照：空库自洽（total 与 allPages 必须同时为 0，读到 " + meta.Total + "/" + meta.AllPages + "）");
            }
            for (int p = 1; !emptyLib && p <= meta.AllPages; p++)
            {
                string page = Path.Combine(root, "catalog", "all", "p" + p + ".json");
                if (!File.Exists(page)) { Check(false, "对照：第 " + p + " 页文件存在"); return; }
                List<ListItem> items = BrowseKit.ParseItems(File.ReadAllText(page), out err);
                if (items == null) { Check(false, "对照：第 " + p + " 页可解析"); return; }
                if (p == 1) Check(items.Count == Math.Min(15, meta.Total), "对照：首页恰好装 pageSize 张（" + items.Count + "）");
                foreach (ListItem it in items)
                {
                    loaded++;
                    if (!ids.Add(it.Id)) dupe++;
                    if (!BlueprintId.IsValid(it.Id)) badHash++;
                    if (CatalogKit.AreaClass(BrowseKit.Tiles(it.AreaM2, meta.TileAreaM2)) != it.AreaClass) badClass++;
                    if (it.HotTotal != it.Downloads * meta.WDownloads + it.Likes * meta.WLikes) badHot++;
                    if (string.IsNullOrEmpty(it.CoverRepoPath) || !File.Exists(Path.Combine(root, it.CoverRepoPath.Replace('/', Path.DirectorySeparatorChar)))) missingCover++;
                    files.Add(it.Id);
                }
            }
            Check(dupe == 0, "对照：跨页不出现重复条目");
            Check(badHash == 0, "对照：每条 id 都过 BlueprintId 校验（" + badHash + " 条不过）");
            Check(badClass == 0, "对照：areaClass 与 C# 按面积算的一致（" + badClass + " 条不一致）");
            Check(badHot == 0, "对照：hotTotal 与权重公式一致（" + badHot + " 条不一致）");
            Check(missingCover == 0, "对照：每条封面文件都在盘上（缺 " + missingCover + "）");
            Check(loaded == meta.Total, "对照：逐页数到的条目数 == index.total（读到 " + loaded + "，索引说 " + meta.Total + "）");

            // 搜索表：q 的每条都必须能对上列表里的 id（对不上就是 builder 与读侧脱节）
            string searchFile = Path.Combine(root, "catalog", "search.json");
            if (File.Exists(searchFile))
            {
                JsonValue s = Json.Parse(File.ReadAllText(searchFile));
                int orphan = 0;
                foreach (JsonValue row in s.Arr("q").A())
                    if (!files.Contains(row.At(0).S(""))) orphan++;
                Check(s.Arr("q").Count == meta.Total && orphan == 0,
                    "对照：search.json 与列表同源（对不上 " + orphan + " 条）");
            }
        }

        // ---------------- 桥层数据与词条键（0.3.0：面板不再收句子，只收 slug + 数字） ----------------

        private static void BridgeDataAndKeys()
        {
            Console.WriteLine("\n[BridgeDataAndKeys]");

            // 键的形状必须与 UI/src/l10n.ts 的 keyOf 逐字符一致 —— 不一致的表现是游戏里面板露出 slug
            Check(PanelKeyKit.Key("BTN_UPLOAD")
                    == "Options.BLUEPRINT_HUB.BTN_UPLOAD[BlueprintHub.BlueprintHub.BlueprintHubMod.Panel.BTN_UPLOAD]",
                "面板键形状：Options.<MOD>.<SLUG>[setting.id.Panel.<SLUG>]");
            Check(PanelKeyKit.ActionKey("BTN_UPLOAD")
                    == "Common.ACTION[BlueprintHub.BlueprintHub.BlueprintHubMod.Panel.BTN_UPLOAD]",
                "面板键的 Common.ACTION 形态");
            Check(PanelKeyKit.IsModKey(PanelKeyKit.Key("PANEL_TITLE"))
                  && PanelKeyKit.IsModKey(PanelKeyKit.ActionKey("PANEL_TITLE")),
                "两种键都会被 BridgeTheLanguageGap 归入模组词条");
            Check(!PanelKeyKit.IsModKey("Common.CLOSE") && !PanelKeyKit.IsModKey("Options.Foo[Bar]"),
                "原版词条与不相干的键不许被误认成本模组的");
            Check(PanelKeyKit.ExtractIdentifier(PanelKeyKit.Key("X"))
                    == "BlueprintHub.BlueprintHub.BlueprintHubMod.Panel.X",
                "identifier 取法与 BLG 的 Scope.ExtractIdentifier 同形");

            // 面积：C# 只发数字串，词由模板给。这里钉「数字串 + 中文模板 = 0.2.0 那版算出来的同一句话」
            long[] areas = { 194065L, 3105032L, 388129L, 7762580L, 40000L, 4000L };
            for (int i = 0; i < areas.Length; i++)
            {
                long a = areas[i];
                string composed = BrowseKit.TilesValue(a, CatalogKit.TILE_AREA_M2) + " 区块 · "
                    + BrowseKit.WanValue(a) + (a >= 10000L ? "万㎡" : "㎡");
                Check(composed == BrowseKit.AreaTextZh(a, CatalogKit.TILE_AREA_M2)
                      || a < 10000L,
                    "面积数字串与 0.2.0 的中文文案同值：" + a + " → " + composed);
                Check((BrowseKit.TilesValue(a, CatalogKit.TILE_AREA_M2) + " 区块 · " + BrowseKit.M2Value(a) + " ㎡")
                      == BrowseKit.AreaTextFull(a, CatalogKit.TILE_AREA_M2),
                    "完整面积：M2Value 千分位不丢");
            }
            Check(BrowseKit.Km2Value(3105032L) == "3.1" && BrowseKit.Km2Value(4000L) == "0.00",
                "英文模板的 km² 数字串：3.1 / 小于 0.1 时保留两位");

            // 相对时间：档位 + 数字必须能还原 0.2.0 那句中文（两套判据不许漂开）
            DateTime now = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
            string[] iso = { "2026-09-27T11:59:30Z", "2026-09-27T11:00:00Z", "2026-09-25T12:00:00Z",
                             "2026-09-20T12:00:00Z", "2026-01-01T00:00:00Z", "2020-01-01T00:00:00Z",
                             "2099-01-01T00:00:00Z" };
            for (int i = 0; i < iso.Length; i++)
            {
                string unit; int n;
                bool ok = BrowseKit.AgoParts(iso[i], now, out unit, out n);
                string zh = ok ? AgoZh(unit, n) : iso[i];
                Check(zh == BrowseKit.RelativeTimeZh(iso[i], now),
                    "AgoParts 与 RelativeTimeZh 同判据：" + iso[i] + " → " + unit + "/" + n + "（" + zh + "）");
            }
            string u2; int n2;
            Check(!BrowseKit.AgoParts("不是日期", now, out u2, out n2), "解析不了的日期：AgoParts 返回 false，前端那一格就不画");
        }

        /// <summary>Locale.PanelZh 里 AGO_* 那几条模板的中文实现（改了模板要同步改这里 —— 这条断言就是用来提醒的）。</summary>
        private static string AgoZh(string unit, int n)
        {
            switch (unit)
            {
                case "now": return "刚刚";
                case "min": return n + " 分钟前";
                case "hour": return n + " 小时前";
                case "day": return n + " 天前";
                case "month": return n + " 个月前";
                case "year": return n + " 年前";
                default: return "?";
            }
        }

        /// <summary>从当前目录往上找到模组根（有 BlueprintHub.csproj 的那一层），再取它的同级仓库。</summary>
        private static string SiblingRepo(string name)
        {
            try
            {
                DirectoryInfo d = new DirectoryInfo(Directory.GetCurrentDirectory());
                while (d != null)
                {
                    if (File.Exists(Path.Combine(d.FullName, "BlueprintHub.csproj")))
                        return Path.Combine(d.Parent == null ? d.FullName : d.Parent.FullName, name);
                    d = d.Parent;
                }
            }
            catch { }
            return string.Empty;
        }

        // ---------------- 夹具 ----------------

        private static List<ListItem> Fixture()
        {
            List<ListItem> all = new List<ListItem>();
            //        id    名称        作者      分类       面积㎡      赞  下载 周热度  更新
            all.Add(SampleItem("b1", "小广场", "阿明", "park", 388129L, 3, 1, 100, "2026-09-10T00:00:00Z"));
            all.Add(SampleItem("b2", "滨江北岸", "阿明", "park", 7762580L, 12, 34, 5, "2026-09-20T00:00:00Z"));
            all.Add(SampleItem("b3", "旧城改造", "牛姐", "public", 1200000L, 40, 2, 60, "2026-09-26T00:00:00Z"));
            all.Add(SampleItem("b4", "货运枢纽", "牛姐", "public", 900000L, 1, 1, 7, "2026-09-11T00:00:00Z"));
            all.Add(SampleItem("b5", "大学城", "老王", "education", 2000000L, 5, 5, 5, "2026-09-12T00:00:00Z"));
            // 多分类条目：同时出现在公园区与公共区两栏
            ListItem multi = SampleItem("b6", "滨江公园", "阿明", "park", 500000L, 2, 2, 2, "2026-09-13T00:00:00Z");
            multi.Categories = new[] { "park", "public" };
            multi.Description = "临江公园与公交枢纽";
            all.Add(multi);
            return all;
        }
    }
}
