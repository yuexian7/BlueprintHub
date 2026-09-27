import React, { useEffect, useRef, useState } from "react";
import { C, IconArea, IconDownload, IconHeart, IconSearch, IconSpinner, IconWarning } from "./icons";
import { avatarColor, CAT_COLOR, cmd, ItemCard, Option, placeholderCover, UiState } from "./api";

/**
 * 浏览区（需求 2 + 需求 3）。所有筛选/排序/翻页都只发一条 Cmd 给 C#，
 * 面板自己不持有任何列表状态 —— C# 是唯一真值源，重开面板也一定是同一个视图。
 */

export const SideBar = ({ st }: { st: UiState }) => (
  <div className="bph-side">
    <div className="bph-side-h">社区类型</div>
    {st.categories.map((cat) => (
      <div
        key={cat.id}
        className={"bph-cat" + (cat.selected ? " bph-cat-on" : "")}
        title={cat.definition}
        onClick={() => cmd("cat", cat.id)}
      >
        <span className="bph-cat-dot" style={{ background: CAT_COLOR[cat.id] || C.accent }} />
        <span className="bph-cat-name">{cat.label}</span>
        <span className="bph-cat-n">{cat.count > 0 ? String(cat.count) : ""}</span>
      </div>
    ))}
  </div>
);

const Chevron = () => (
  <svg width="10" height="10" viewBox="0 0 24 24" style={{ marginLeft: "5rem" }} xmlns="http://www.w3.org/2000/svg">
    <path d="M6 9.5 12 15.5 18 9.5" fill="none" stroke={C.dim} strokeWidth="2.4" strokeLinecap="round" />
  </svg>
);

export const Dropdown = ({
  label, current, options, onPick,
}: {
  label: string;
  current: string;
  options: Option[];
  onPick: (id: string) => void;
}) => {
  const [open, setOpen] = useState(false);
  const active = options.filter((o) => o.id === current)[0] || options[0];
  return (
    <div className="bph-drop">
      <div className="bph-drop-btn" onClick={() => setOpen(!open)}>
        <span className="bph-drop-cur">{label}</span>
        <span>{active ? active.label : ""}</span>
        <Chevron />
      </div>
      {open ? (
        <>
          {/* 透明遮罩负责「点别处就收起」：Cohtml 里 blur 事件时序不稳，用一层覆盖全屏的 div 更可靠 */}
          <div
            style={{ position: "absolute", left: 0, top: 0, width: "100%", height: "100%", zIndex: 590, pointerEvents: "auto" }}
            onClick={() => setOpen(false)}
          />
          <div className="bph-drop-menu">
            {options.map((o) => (
              <div
                key={o.id}
                className={"bph-drop-item" + (o.id === current ? " bph-drop-item-on" : "")}
                onClick={() => { setOpen(false); onPick(o.id); }}
              >
                {o.label}
              </div>
            ))}
          </div>
        </>
      ) : null}
    </div>
  );
};

export const MenuBar = ({ st }: { st: UiState }) => {
  const m = st.menu!;
  const [text, setText] = useState(m.search);
  const timer = useRef<number | null>(null);

  // C# 改了条件（例如点「清空筛选」）时把输入框拉回来，否则前端会留着后端已经不用的词
  useEffect(() => { setText(m.search); }, [m.search]);
  useEffect(() => () => { if (timer.current !== null) window.clearTimeout(timer.current); }, []);

  const push = (v: string) => {
    if (timer.current !== null) window.clearTimeout(timer.current);
    timer.current = window.setTimeout(() => cmd("search", v), 260);   // 打字不逐字符打后端
  };

  return (
    <div className="bph-menu">
      <span className="bph-tile">{m.tileHint}</span>
      <span className="bph-goto">· 面积越大套用越久</span>
      <div className="bph-search">
        <IconSearch size={15} />
        <input
          type="text"
          value={text}
          placeholder="搜索蓝图名称 / 作者 / 描述"
          onChange={(e) => { setText(e.target.value); push(e.target.value); }}
        />
      </div>
      <div className="bph-spacer" />
      <Dropdown label="面积" current={m.area} options={m.areas} onPick={(id) => cmd("area", id)} />
      <Dropdown label="排序" current={m.sort} options={m.sorts} onPick={(id) => cmd("sort", id)} />
    </div>
  );
};

export const Card = ({ it }: { it: ItemCard }) => {
  const cover = it.cover || placeholderCover(it.cats || "", it.id);
  const catColor = CAT_COLOR[((it.cats || "").split(",")[0] || "").trim()] || C.accent;
  return (
    <div className="bph-card" onClick={() => cmd("detail", it.id)}>
      <div className="bph-cover">
        <img src={cover} alt="" />
        <span className="bph-catbar" style={{ background: catColor }} />
        <div className="bph-strip">
          <span className="bph-area" title={it.areaFull || it.areaText}>{it.areaText}</span>
          <div className={"bph-stat" + (it.liked ? " bph-stat-on" : "")}
            title={it.liked ? "已经点过一次：每张蓝图只能加一次" : "点赞"}
            onClick={(e) => { e.stopPropagation(); cmd("like", it.id); }}>
            <IconHeart size={13} color={it.liked ? C.voted : C.like} filled={it.liked} />
            <span className="bph-stat-n" style={{ color: it.liked ? C.voted : "#dbe6f0" }}>{it.likesText}</span>
          </div>
          <div className={"bph-stat" + (it.used ? " bph-stat-on" : "")}
            title={it.used ? "已经记过一次" : "套用次数"}
            onClick={(e) => { e.stopPropagation(); cmd("dl", it.id); }}>
            <IconDownload size={13} color={it.used ? C.voted : C.download} />
            <span className="bph-stat-n" style={{ color: it.used ? C.voted : "#dbe6f0" }}>{it.downloadsText}</span>
          </div>
        </div>
      </div>
      <div className="bph-name">{it.name}</div>
      <div className="bph-meta">
        <span className="bph-mini-av" style={{ background: avatarColor(it.authorId || it.author) }}>
          <span className="bph-mini-txt">{(it.author || "?").substr(0, 1).toUpperCase()}</span>
        </span>
        <span className="bph-by">{it.author}</span>
        <span className="bph-when">{it.updated}</span>
      </div>
    </div>
  );
};

const PageCell = ({ n, cur, onGo }: { n: number; cur: number; onGo: (n: number) => void }) => (
  <div className={"bph-pg" + (n === cur ? " bph-pg-on" : "")} onClick={() => onGo(n)}>{String(n)}</div>
);

const Arrow = ({ left, off, onGo }: { left: boolean; off: boolean; onGo: () => void }) => (
  <div className={"bph-pg" + (off ? " bph-pg-off" : "")} onClick={() => { if (!off) onGo(); }}>
    <svg width="12" height="12" viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">
      <path d={left ? "M15 5l-7 7 7 7" : "M9 5l7 7-7 7"} fill="none" stroke={off ? C.dim : C.text}
        strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  </div>
);

export const Pager = ({ st }: { st: UiState }) => {
  const m = st.menu!;
  if (m.pages <= 0) return null;
  const cells: number[] = [];
  const from = Math.max(1, Math.min(m.page - 2, m.pages - 4));
  for (let i = 0; i < 5; i++) {
    const n = from + i;
    if (n > m.pages) break;
    cells.push(n);
  }
  return (
    <div className="bph-pager">
      <Arrow left off={m.page <= 1} onGo={() => cmd("page", m.page - 1)} />
      {cells.map((n) => <PageCell key={n} n={n} cur={m.page} onGo={(v) => cmd("page", v)} />)}
      <Arrow left={false} off={m.page >= m.pages} onGo={() => cmd("page", m.page + 1)} />
      <span className="bph-count">
        共 {m.total} 张{st.truncated ? "（工坊条目过多，面板只取前 300 张）" : ""}
      </span>
    </div>
  );
};

/** 四态：加载中 / 库里没东西 / 条件太严 / 网络或校验失败。需求 6「报错一定要有提示」。 */
export const StateArea = ({ st }: { st: UiState }) => {
  if (st.status === "loading") {
    return (
      <div className="bph-state">
        <div className="bph-spin"><IconSpinner size={44} /></div>
        <div className="bph-state-t">正在读取工坊…</div>
        <div className="bph-state-s">{st.statusText || "首次打开要拉索引与列表，之后走本地缓存。"}</div>
      </div>
    );
  }
  if (st.status === "error") {
    return (
      <div className="bph-state">
        <IconWarning size={40} color={C.danger} />
        <div className="bph-state-t" style={{ color: "#ffd9da" }}>工坊连接失败</div>
        <div className="bph-state-s">{st.statusText}</div>
        <div className="bph-btn-row">
          <div className="bph-btn bph-btn-warn" onClick={() => cmd("retry")}>重试</div>
        </div>
      </div>
    );
  }
  if (st.status === "empty") {
    return (
      <div className="bph-state">
        <IconArea size={40} color={C.dim} />
        <div className="bph-state-t">工坊里还没有蓝图</div>
        <div className="bph-state-s">
          v0.1.0 的上传走「游戏内导出 → 向工坊仓库提 PR」：作者在游戏里把一块市辖区导出成蓝图目录，
          提交后经自动校验通过，就会出现在这个列表里。
        </div>
      </div>
    );
  }
  return (
    <div className="bph-state">
      <IconSearch size={34} color={C.dim} />
      <div className="bph-state-t">没有符合条件的蓝图</div>
      <div className="bph-state-s">换个关键词，或者把面积筛选恢复成「全部」。</div>
      <div className="bph-btn-row">
        <div className="bph-btn" onClick={() => { cmd("search", ""); cmd("area", "all"); }}>清空筛选</div>
      </div>
    </div>
  );
};

export const BrowseBody = ({ st }: { st: UiState }) => (
  <div className="bph-body">
    <SideBar st={st} />
    <div className="bph-main">
      <MenuBar st={st} />
      {st.status === "ready" && st.items.length > 0 ? (
        <>
          <div className="bph-grid">
            {st.items.map((it) => <Card key={it.id} it={it} />)}
          </div>
          <Pager st={st} />
        </>
      ) : (
        <StateArea st={st} />
      )}
    </div>
  </div>
);
