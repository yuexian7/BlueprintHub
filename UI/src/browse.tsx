import React, { useEffect, useRef, useState } from "react";
import { C, IconArea, IconDownload, IconHeart, IconSearch, IconSpinner, IconWarning } from "./icons";
import { areaArgs, areaSlugFor, avatarColor, CAT_COLOR, cmd, coverPlan, ItemCard, Option, UiState } from "./api";
import { LArgs, Translate, prettyCount } from "./l10n";

/**
 * 浏览区。两条纪律：
 *  · 所有筛选/排序/翻页只发一条 Cmd 给 C#，面板自己不持有任何列表状态（C# 是唯一真值源，重开面板一定是同一个视图）。
 *  · 句子和单位词一律来自 t(slug)（游戏本地化词典），本文件里**一个玩家可见的中英文字都不许出现**。
 */

export const SideBar = ({ st, t }: { st: UiState; t: Translate }) => (
  <div className="bph-side">
    <div className="bph-side-h">{t("TYPE_TITLE")}</div>
    {st.categories.map((cat) => (
      <div
        key={cat.id}
        className={"bph-cat" + (cat.selected ? " bph-cat-on" : "")}
        title={t(cat.descSlug)}
        onClick={() => cmd("cat", cat.id)}
      >
        <span className="bph-cat-dot" style={{ background: CAT_COLOR[cat.id] || C.accent }} />
        <span className="bph-cat-name">{cat.slug ? t(cat.slug) : cat.label}</span>
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
  label, current, options, onPick, t,
}: {
  label: string;
  current: string;
  options: Option[];
  onPick: (id: string) => void;
  t: Translate;
}) => {
  const [open, setOpen] = useState(false);
  const active = options.filter((o) => o.id === current)[0] || options[0];
  return (
    <div className="bph-drop">
      <div className="bph-drop-btn" onClick={() => setOpen(!open)}>
        <span className="bph-drop-cur">{label}</span>
        <span>{active ? (active.slug ? t(active.slug) : active.label) : ""}</span>
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
                {o.slug ? t(o.slug) : o.label}
              </div>
            ))}
          </div>
        </>
      ) : null}
    </div>
  );
};

export const MenuBar = ({ st, t }: { st: UiState; t: Translate }) => {
  const m = st.menu!;
  const [text, setText] = useState(m.search);
  const [focus, setFocus] = useState(false);
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
      <span className="bph-tile">{t("TILE_HINT", { tilem2: st.tileM2 || "388,129" })}</span>
      {/* :focus-within 在 Cohtml 里不认（实机日志点名），所以高亮框靠自己的 focus 状态类 */}
      <div className={"bph-search" + (focus ? " bph-search-on" : "")}>
        <IconSearch size={15} />
        <input
          type="text"
          value={text}
          placeholder={t("SEARCH_PLACEHOLDER")}
          onChange={(e) => { setText(e.target.value); push(e.target.value); }}
          onFocus={() => setFocus(true)}
          onBlur={() => setFocus(false)}
        />
      </div>
      <div className="bph-spacer" />
      <Dropdown label={t("AREA_TITLE")} current={m.area} options={m.areas} onPick={(id) => cmd("area", id)} t={t} />
      <Dropdown label={t("SORT_TITLE")} current={m.sort} options={m.sorts} onPick={(id) => cmd("sort", id)} t={t} />
    </div>
  );
};

/** 无封面时的占位图案：内联 `<svg>`（不吃 data URI，见 api.coverPlan 的注释）。 */
const CoverPlaceholder = ({ cats, id }: { cats: string; id: string }) => {
  const plan = coverPlan(cats, id);
  return (
    <svg className="bph-cover-svg" viewBox="0 0 100 70" preserveAspectRatio="none" xmlns="http://www.w3.org/2000/svg">
      <rect width="100" height="70" fill="#101a26" />
      <g fill="none" stroke="#ffffff" strokeOpacity="0.07" strokeWidth="0.4">
        <path d="M0 23 H100M0 60 H100M67 0 V70" />
      </g>
      <g fill={plan.color} opacity="0.85">
        {plan.blocks.map((b, i) => <rect key={i} x={b.x} y={b.y} width={b.w} height={b.h} rx="1.5" opacity={b.o} />)}
      </g>
      <g fill="none" stroke={plan.color} strokeWidth="0.7" opacity="0.55">
        <path d="M0 46 H100" />
        <path d="M34 0 V70" />
      </g>
    </svg>
  );
};

/** 相对时间：档位与数字由 C# 算（BrowseKit.AgoParts，离线测过），这里只把词交给词典。 */
const agoText = (t: Translate, it: ItemCard): string => {
  if (!it.agoUnit) return "";
  const args: LArgs = { n: it.agoN };
  return t("AGO_" + it.agoUnit.toUpperCase(), args);
};

export const Card = ({ it, t, lang }: { it: ItemCard; t: Translate; lang: string }) => {
  const catColor = CAT_COLOR[((it.cats || "").split(",")[0] || "").trim()] || C.accent;
  const areaText = t(areaSlugFor(it), areaArgs(it));
  const nick = it.author || t("ANONYMOUS");
  return (
    <div className="bph-card" onClick={() => cmd("detail", it.id)}>
      <div className="bph-cover">
        {it.cover ? <img src={it.cover} alt="" /> : <CoverPlaceholder cats={it.cats || ""} id={it.id} />}
        <span className="bph-catbar" style={{ background: catColor }} />
        <div className="bph-strip">
          {/* title 是玩家看得见摸得着的悬停提示，必须走词条：0.3.0 这里是拼死串的「388,129 ㎡」 */}
          <span className="bph-area" title={t("CARD_AREA_TINY", { m2: it.m2 })}>{areaText}</span>
          <div className={"bph-stat bph-stat-push" + (it.liked ? " bph-stat-on" : "")}
            title={it.liked ? t("CARD_LIKED_TIP") : t("CARD_LIKE_TIP")}
            onClick={(e) => { e.stopPropagation(); cmd("like", it.id); }}>
            <IconHeart size={13} color={it.liked ? C.voted : C.like} filled={it.liked} />
            <span className="bph-stat-n" style={{ color: it.liked ? C.voted : "#dbe6f0" }}>{pretty(it.likes, lang)}</span>
          </div>
          <div className={"bph-stat" + (it.used ? " bph-stat-on" : "")}
            title={it.used ? t("CARD_USED_TIP") : t("CARD_USE_TIP")}
            onClick={(e) => { e.stopPropagation(); cmd("dl", it.id); }}>
            <IconDownload size={13} color={C.download} />
            <span className="bph-stat-n" style={{ color: it.used ? C.voted : "#dbe6f0" }}>{pretty(it.downloads, lang)}</span>
          </div>
        </div>
      </div>
      <div className="bph-name" title={it.desc || it.name}>{it.name}</div>
      <div className="bph-meta">
        <span className="bph-mini-av" style={{ background: avatarColor(it.authorId || it.author) }}>
          <span className="bph-mini-txt">{nick.substr(0, 1).toUpperCase()}</span>
        </span>
        <span className="bph-by">{nick}</span>
        <span className="bph-when">{agoText(t, it)}</span>
      </div>
    </div>
  );
};

// 计数缩写（原版 UI 同口径）
const pretty = (n: number, lang: string): string => prettyCount(n, lang);

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

export const Pager = ({ st, t }: { st: UiState; t: Translate }) => {
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
        {t("PAGER_TOTAL", { total: m.total })}
        {st.truncated ? " · " + t("TRUNC_NOTE", { max: st.maxItems || 300 }) : ""}
      </span>
    </div>
  );
};

/** 四态：加载中 / 库里没东西 / 条件太严 / 网络或校验失败。报错一定要给一句人话 + 一行技术信息 + 一个能按的按钮。 */
export const StateArea = ({ st, t }: { st: UiState; t: Translate }) => {
  if (st.status === "loading") {
    return (
      <div className="bph-state">
        <div className="bph-spin"><IconSpinner size={44} /></div>
        <div className="bph-state-t">{t("STATUS_LOADING")}</div>
        {st.statusDetail ? <div className="bph-state-s">{st.statusDetail}</div> : null}
      </div>
    );
  }
  if (st.status === "error") {
    return (
      <div className="bph-state">
        <IconWarning size={40} color={C.danger} />
        <div className="bph-state-t">{t(st.statusSlug || "ERR_TITLE")}</div>
        {st.statusDetail ? <div className="bph-state-tech">{st.statusDetail}</div> : null}
        <div className="bph-btn-row">
          <div className="bph-btn bph-btn-warn" onClick={() => cmd("retry")}>{t("BTN_RETRY")}</div>
        </div>
      </div>
    );
  }
  if (st.status === "empty") {
    return (
      <div className="bph-state">
        <IconArea size={40} color={C.dim} />
        <div className="bph-state-t">{t("EMPTY_TITLE")}</div>
        <div className="bph-state-s">{t("EMPTY_HINT")}</div>
      </div>
    );
  }
  return (
    <div className="bph-state">
      <IconSearch size={34} color={C.dim} />
      <div className="bph-state-t">{t("NORESULT_TITLE")}</div>
      <div className="bph-state-s">{t("NORESULT_HINT")}</div>
      <div className="bph-btn-row">
        <div className="bph-btn" onClick={() => { cmd("search", ""); cmd("area", "all"); }}>{t("BTN_CLEAR")}</div>
      </div>
    </div>
  );
};

export const BrowseBody = ({ st, t }: { st: UiState; t: Translate }) => (
  <div className="bph-body">
    <SideBar st={st} t={t} />
    <div className="bph-main">
      <MenuBar st={st} t={t} />
      {st.status === "ready" && st.items.length > 0 ? (
        <>
          <div className="bph-grid">
            {st.items.map((it) => <Card key={it.id} it={it} t={t} lang={st.lang} />)}
          </div>
          <Pager st={st} t={t} />
        </>
      ) : (
        <StateArea st={st} t={t} />
      )}
    </div>
  </div>
);
