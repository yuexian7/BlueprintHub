import React from "react";
import { cmd, useUiState, type UploadState } from "./api";
import { useT, type LArgs } from "./l10n";
import { IconUpload, IconClose, IconWarning, IconSpinner } from "./icons";

/**
 * 市辖区面板底部那一排里的「上传蓝图」条目（需求 2）。
 *
 * 这条目是本模组 C# 侧的一个 Game.UI.InGame.InfoSectionBase（DistrictUploadSection），
 * 由 SelectedInfoUISystem.AddBottomSection 注册；游戏把它序列化成一个 section，
 * **键 = 那个 C# 类的全名**（FACT：InfoSectionBase.Write 用 writer.TypeBegin(GetType().FullName)）。
 * 所以 index.tsx 里必须用同一个字符串去 extend selectedInfoSectionComponents —— 类名改了这里就得改。
 *
 * props 全部来自 DistrictUploadSection.OnWriteProperties，都是字符串（游戏的 JSON 通道就这么窄）：
 *   districtName / areaU / phase / detail / draftPath / bpId / missing / bytes / loggedIn
 */

export const DISTRICT_SECTION_TYPE = "BlueprintHub.Systems.UI.DistrictUploadSection";

/** C# 传过来的 section 属性；每一项都可能是 undefined（词条没写、字段缺），所以全部按可选处理。 */
export interface DistrictSectionProps {
  districtName?: string;
  areaU?: string;
  phase?: string;
  detail?: string;
  draftPath?: string;
  bpId?: string;
  missing?: string;
  bytes?: string;
  loggedIn?: string;
  [k: string]: unknown;
}

/** 采集失败的那些键 → 玩家看得懂的词条。too-big:* 单独走「太大」那一句。 */
function failSlug(detail: string): string {
  if (!detail) return "DUPLOAD_FAIL";
  if (detail.indexOf("too-big") === 0) return "DUPLOAD_TOO_BIG";
  if (detail === "not-district" || detail === "empty") return "DUPLOAD_NONE";
  return "DUPLOAD_FAIL";
}

/**
 * 失败键的占位符参数。C# 给的形状是 too-big:<哪一类>:<多少>:<上限>（DistrictCapture.Oversize），
 * 「太大」那句要的是后两个数 —— 上限是**条目数**不是面积 u，所以句子和这里必须一起改，
 * 不然玩家会看到「太大（u）」这种驴唇不对马嘴的提示。
 */
function failArgs(detail: string, path: string): LArgs {
  const seg = String(detail || "").split(":");
  const a: LArgs = { err: seg[0] || detail || "", path: path || "" };
  if (seg[0] === "too-big") { a.n = seg[2] || ""; a.max = seg[3] || ""; }
  return a;
}

const UploadForm = ({ p, onClose }: { p: DistrictSectionProps; onClose: () => void }) => {
  const t = useT();
  const st = useUiState();
  const cats = (st.categories || []).filter((c) => c.id && c.id !== "all");
  const [name, setName] = React.useState<string>(p.districtName || "");
  const [cat, setCat] = React.useState<string>(cats.length ? cats[0].id : "residential");
  const [desc, setDesc] = React.useState<string>("");

  const submit = () => {
    // 命令形状 upload|名称|类型|简介 —— C# 按前两个 | 切三段，简介允许含 |（见 SubmitUpload）
    cmd("upload", (name || "").trim() + "|" + cat + "|" + (desc || "").trim());
    onClose();
  };

  return (
    <div className="bph-form">
      <div className="bph-form-row">
        <span className="bph-form-label">{t("DUPLOAD_NAME_LABEL")}</span>
        <input className="bph-input" type="text" value={name} maxLength={48}
          onInput={(e: any) => setName(String(e?.target?.value ?? ""))}
          onChange={(e: any) => setName(String(e?.target?.value ?? ""))} />
      </div>
      <div className="bph-form-row">
        <span className="bph-form-label">{t("DUPLOAD_CAT_LABEL")}</span>
        <div className="bph-chips">
          {cats.map((c) => (
            <button key={c.id} type="button"
              className={"bph-chip" + (cat === c.id ? " bph-chip-on" : "")}
              onClick={() => setCat(c.id)}>{t(c.slug)}</button>
          ))}
        </div>
      </div>
      <div className="bph-form-row">
        <span className="bph-form-label">{t("DUPLOAD_DESC_LABEL")}</span>
        <textarea className="bph-input bph-textarea" value={desc} maxLength={2000} rows={2}
          onInput={(e: any) => setDesc(String(e?.target?.value ?? ""))}
          onChange={(e: any) => setDesc(String(e?.target?.value ?? ""))} />
      </div>
      <div className="bph-form-foot">
        <button type="button" className="bph-btn bph-btn-main" onClick={submit}>{t("DUPLOAD_SUBMIT")}</button>
        <button type="button" className="bph-btn" onClick={onClose}>{t("DUPLOAD_CANCEL")}</button>
      </div>
    </div>
  );
};

const DoneBlock = ({ p }: { p: DistrictSectionProps }) => {
  const t = useT();
  const box = React.useRef<HTMLInputElement | null>(null);
  const select = () => { try { const el = box.current; if (el) el.select(); } catch (e) { /* 选不中也还能用鼠标 */ } };
  const missing = parseInt(p.missing || "0", 10) || 0;
  return (
    <div className="bph-done">
      <div className="bph-done-title">{t("DUPLOAD_OK", { path: p.draftPath || "" })}</div>
      {missing > 0 ? <div className="bph-warn-line"><IconWarning size={14} />{t("DUPLOAD_PARTIAL", { n: String(missing) })}</div> : null}
      <div className="bph-hint-line">{t("DUPLOAD_OUTBOX")}</div>
      <div className="bph-path-row">
        <input ref={box} className="bph-input bph-path" type="text" readOnly value={p.draftPath || ""} />
        <button type="button" className="bph-btn" onClick={select}>{t("DUPLOAD_COPY")}</button>
      </div>
    </div>
  );
};

export const DistrictUploadSectionView = (props: DistrictSectionProps) => {
  const t = useT();
  const st = useUiState();
  const [open, setOpen] = React.useState<boolean>(false);
  const phase: string = (props.phase || st.upload?.phase || "idle") as string;
  const p: DistrictSectionProps = {
    ...props,
    phase,
    draftPath: props.draftPath ?? st.upload?.path,
    bpId: props.bpId ?? st.upload?.bpId,
    missing: props.missing ?? String(st.upload?.missing ?? 0),
    detail: props.detail ?? st.upload?.detail,
    loggedIn: props.loggedIn ?? (st.account?.loggedIn ? "1" : "0"),
  };

  const busy = phase === "busy";
  const done = phase === "done";
  const failed = phase === "failed";
  const signed = p.loggedIn === "1";

  return (
    <div className="bph-dpanel">
      {done ? <DoneBlock p={p} /> : null}
      {failed ? (
        <div className="bph-fail">
          <IconWarning size={14} />
          <span>{t(failSlug(p.detail || ""), failArgs(p.detail || "", p.draftPath || ""))}</span>
        </div>
      ) : null}
      {!done && !failed && open ? <UploadForm p={p} onClose={() => setOpen(false)} /> : null}
      {!done && !busy && (
        <div className="bph-drow">
          <button type="button" className={"bph-btn bph-btn-main" + (open ? " bph-btn-off" : "")}
            disabled={!signed}
            title={signed ? t("DUPLOAD_TITLE") : t("ACCOUNT_LOGIN_TIP")}
            onClick={() => { if (signed) setOpen(!open); }}>
            {open ? <IconClose size={14} /> : <IconUpload size={14} />}
            <span>{open ? t("DUPLOAD_CANCEL") : t("DUPLOAD_BTN")}</span>
          </button>
          {p.areaU ? <span className="bph-darea">{p.areaU}u</span> : null}
          {!signed ? <span className="bph-dneed">{t("ACCOUNT_LOGIN_TIP")}</span> : null}
        </div>
      )}
      {busy ? <div className="bph-busy"><IconSpinner size={14} /><span>{t("DUPLOAD_BUSY")}</span></div> : null}
    </div>
  );
};

/** 主面板里的兜底入口：市辖区那条钩子没挂上时才出现（游戏大版本改了内部路径也不能把玩家堵死）。 */
export const DistrictFallbackHint = () => {
  const t = useT();
  const st = useUiState();
  if (st.hooks?.districtSection) return null;
  const u: UploadState = st.upload || { phase: "idle", detail: "", path: "", bpId: "", name: "", cover: "", missing: 0, bytes: 0 };
  return (
    <div className="bph-fallback">
      <div className="bph-fallback-title">{t("UPLOAD_HINT")}</div>
      <div className="bph-fallback-body">
        <button type="button" className="bph-btn bph-btn-main"
          disabled={!st.account?.loggedIn || u.phase === "busy"}
          onClick={() => cmd("upload", (u.name || "") + "|residential|")}>
          {t("DUPLOAD_BTN")}
        </button>
        {u.phase === "failed" ? <span className="bph-fail-inline">{t(failSlug(u.detail), failArgs(u.detail, u.path))}</span> : null}
        {u.phase === "done" ? <span className="bph-ok-inline">{u.path}</span> : null}
      </div>
    </div>
  );
};
