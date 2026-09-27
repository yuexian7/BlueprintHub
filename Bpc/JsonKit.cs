using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BlueprintHub.Bpc
{
    /// <summary>
    /// 极简 JSON 读写。为什么不用现成的：
    ///  · 游戏 Managed 目录里只有 Newtonsoft.Json.dll（FACT：列目录实测，**没有** Colossal.Json.dll），
    ///    引它 = 赌运行时解析得到 + 版本兼容；而 catalog / meta.json / votes.json 全是扁平结构，
    ///    自己写换来「零外部依赖 + 纯逻辑可离线测」（Playbook 硬规则 14）。
    ///  · 这个文件一个游戏类型都不许出现，tests/ 的离线壳直接 Compile 它。
    /// 解析器故意宽容：字段缺失一律走默认值 —— 工坊数据面加新字段不许把老模组打挂。
    /// </summary>
    public sealed class JsonException : Exception
    {
        public JsonException(string message) : base(message) { }
    }

    public sealed class JsonValue
    {
        public enum Kind { Null, Bool, Number, String, Array, Object }

        public readonly Kind Type;
        private readonly bool _bool;
        private readonly double _num;
        private readonly string _str;
        private readonly List<JsonValue> _arr;
        private readonly Dictionary<string, JsonValue> _obj;
        private readonly List<string> _order;      // 对象键保持书写顺序（面板列资产表时要稳定）

        internal JsonValue(Kind t, bool b, double n, string s, List<JsonValue> a,
            Dictionary<string, JsonValue> o, List<string> order)
        {
            Type = t; _bool = b; _num = n; _str = s; _arr = a; _obj = o; _order = order;
        }

        public static readonly JsonValue NULL = new JsonValue(Kind.Null, false, 0d, null, null, null, null);

        public bool IsObject { get { return Type == Kind.Object; } }
        public bool IsArray { get { return Type == Kind.Array; } }
        public bool IsString { get { return Type == Kind.String; } }
        public bool IsNumber { get { return Type == Kind.Number; } }
        public bool IsNull { get { return Type == Kind.Null; } }

        public int Count
        {
            get { return IsArray ? _arr.Count : (IsObject ? _order.Count : 0); }
        }

        /// <summary>越界 / 类型不符一律返回 NULL，调用方再取默认值 —— 不抛。</summary>
        public JsonValue At(int i)
        {
            if (!IsArray || i < 0 || i >= _arr.Count) return NULL;
            return _arr[i];
        }

        public JsonValue G(string key)
        {
            if (!IsObject) return NULL;
            JsonValue v;
            return _obj.TryGetValue(key, out v) ? v : NULL;
        }

        public bool Has(string key) { return IsObject && _obj.ContainsKey(key); }

        public string[] Keys { get { return IsObject ? _order.ToArray() : new string[0]; } }

        public string S(string def = "")
        {
            switch (Type)
            {
                case Kind.String: return _str;
                case Kind.Number: return _num.ToString("R", CultureInfo.InvariantCulture);
                case Kind.Bool: return _bool ? "true" : "false";
                default: return def;
            }
        }

        public double D(double def = 0d)
        {
            if (Type == Kind.Number) return _num;
            if (Type == Kind.String)
            {
                double parsed;
                if (double.TryParse(_str, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return parsed;
            }
            return def;
        }

        public long L(long def = 0L)
        {
            if (Type == Kind.Number) return (long)Math.Round(_num);
            if (Type == Kind.String)
            {
                long parsed;
                if (long.TryParse(_str, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) return parsed;
            }
            return def;
        }

        public int I(int def = 0) { return (int)L(def); }

        public bool B(bool def = false)
        {
            if (Type == Kind.Bool) return _bool;
            if (Type == Kind.Number) return _num != 0d;
            return def;
        }

        public List<JsonValue> A() { return IsArray ? _arr : new List<JsonValue>(); }

        public string[] StrArray(string key)
        {
            List<JsonValue> list = G(key).A();
            string[] outArr = new string[list.Count];
            for (int i = 0; i < list.Count; i++) outArr[i] = list[i].S("");
            return outArr;
        }

        public string Str(string key, string def = "") { return G(key).S(def); }
        public long Long(string key, long def = 0L) { return G(key).L(def); }
        public int Int(string key, int def = 0) { return G(key).I(def); }
        public double Num(string key, double def = 0d) { return G(key).D(def); }
        public bool Bool(string key, bool def = false) { return G(key).B(def); }

        public JsonValue Obj(string key)
        {
            JsonValue v = G(key);
            return v.IsObject ? v : NULL;
        }

        public JsonValue Arr(string key)
        {
            JsonValue v = G(key);
            return v.IsArray ? v : NULL;
        }

        public override string ToString()
        {
            JsonWriter w = new JsonWriter();
            w.Write(this);
            return w.ToString();
        }
    }

    public static class Json
    {
        public static JsonValue Parse(string text)
        {
            string err;
            JsonValue v;
            if (!TryParse(text, out v, out err)) throw new JsonException(err);
            return v;
        }

        public static bool TryParse(string text, out JsonValue value, out string error)
        {
            value = JsonValue.NULL;
            error = null;
            if (string.IsNullOrEmpty(text))
            {
                error = "空文本";
                return false;
            }
            JsonParser p = new JsonParser(text);
            try
            {
                value = p.Value();
                p.SkipWs();                     // 尾部换行/空格不算「多余内容」，否则每个带结尾换行的文件都被判坏
                if (!p.Eof) error = "尾部有多余内容 @" + p.Pos;
                return error == null;
            }
            catch (JsonException ex)
            {
                error = ex.Message;
                value = JsonValue.NULL;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                value = JsonValue.NULL;
                return false;
            }
        }

        /// <summary>带引号的字符串字面量。U+2028/2029 必须转义：它们在 JS 里是行终止符，会让前端 eval 挂掉。</summary>
        public static string Quote(string s)
        {
            StringBuilder sb = new StringBuilder((s == null ? 4 : s.Length) + 8);
            sb.Append('"');
            if (s != null)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < ' ' || c == '\u2028' || c == '\u2029')
                                sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            else sb.Append(c);
                            break;
                    }
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string Escape(string s) { return Quote(s); }
    }

    /// <summary>流式写。Begin/End 必须成对；键用 Key(...) 或 Begin*/Str(...) 的 key 重载，二者不混用。</summary>
    public sealed class JsonWriter
    {
        private readonly StringBuilder _sb = new StringBuilder(512);
        private readonly List<int> _counts = new List<int>();
        private readonly List<bool> _isObj = new List<bool>();
        private string _pendingKey;

        /// <summary>调用者显式放键（下一个值/容器会用掉它）。</summary>
        public JsonWriter Key(string key) { _pendingKey = key; return this; }

        private void Emit()
        {
            if (_counts.Count == 0) return;                 // 根节点
            int top = _counts.Count - 1;
            if (_counts[top] > 0) _sb.Append(',');
            if (_isObj[top])
            {
                _sb.Append(Json.Quote(_pendingKey ?? string.Empty)).Append(':');
                _pendingKey = null;
            }
            _counts[top] = _counts[top] + 1;
        }

        public JsonWriter BeginObj(string key = null)
        {
            if (key != null) _pendingKey = key;
            Emit();
            _sb.Append('{');
            _counts.Add(0);
            _isObj.Add(true);
            return this;
        }

        public JsonWriter BeginArr(string key = null)
        {
            if (key != null) _pendingKey = key;
            Emit();
            _sb.Append('[');
            _counts.Add(0);
            _isObj.Add(false);
            return this;
        }

        public JsonWriter End()
        {
            if (_counts.Count == 0) throw new JsonException("Begin/End 不配对");
            int top = _counts.Count - 1;
            bool wasObj = _isObj[top];
            _isObj.RemoveAt(top);
            _counts.RemoveAt(top);
            _sb.Append(wasObj ? '}' : ']');
            return this;
        }

        public JsonWriter Str(string value) { Emit(); _sb.Append(Json.Quote(value)); return this; }
        public JsonWriter Num(long value) { Emit(); _sb.Append(value.ToString(CultureInfo.InvariantCulture)); return this; }
        public JsonWriter Num(double value) { Emit(); _sb.Append(value.ToString("R", CultureInfo.InvariantCulture)); return this; }
        public JsonWriter Bool(bool value) { Emit(); _sb.Append(value ? "true" : "false"); return this; }
        public JsonWriter Null() { Emit(); _sb.Append("null"); return this; }

        public JsonWriter Str(string key, string value) { Key(key); return Str(value); }
        public JsonWriter Num(string key, long value) { Key(key); return Num(value); }
        public JsonWriter Num(string key, double value) { Key(key); return Num(value); }
        public JsonWriter Bool(string key, bool value) { Key(key); return Bool(value); }
        public JsonWriter Null(string key) { Key(key); return Null(); }

        /// <summary>把已解析的 JsonValue 原样写出（catalog 字段透传时用）。</summary>
        public JsonWriter Write(JsonValue v)
        {
            if (v == null || v.IsNull) return Null();
            switch (v.Type)
            {
                case JsonValue.Kind.Bool: return Bool(v.B());
                case JsonValue.Kind.Number:
                    long asLong = v.L();
                    return Math.Abs(v.D() - asLong) < 1e-9 ? Num(asLong) : Num(v.D());
                case JsonValue.Kind.String: return Str(v.S());
                case JsonValue.Kind.Array:
                    BeginArr();
                    foreach (JsonValue e in v.A()) Write(e);
                    return End();
                default:
                    BeginObj();
                    foreach (string k in v.Keys) { Key(k); Write(v.G(k)); }
                    return End();
            }
        }

        /// <summary>收尾自检：不配对直接抛，别把坏 JSON 推到前端。</summary>
        public string Finish()
        {
            if (_counts.Count != 0) throw new JsonException("JSON 未闭合，剩 " + _counts.Count + " 层");
            return _sb.ToString();
        }

        public override string ToString() { return _sb.ToString(); }
    }

    /// <summary>递归下降。深度上限防递归爆炸（外部数据不可信）。</summary>
    internal sealed class JsonParser
    {
        private const int MAX_DEPTH = 64;
        private readonly string _s;
        private int _i;
        private int _depth;

        public JsonParser(string s) { _s = s; }

        public int Pos { get { return _i; } }
        public bool Eof { get { return _i >= _s.Length; } }

        internal void SkipWs()
        {
            while (_i < _s.Length)
            {
                char c = _s[_i];
                // BOM / 零宽空格也当空白：workshop 的文件偶发带 BOM
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\uFEFF' || c == '\u200B') { _i++; continue; }
                break;
            }
        }

        public JsonValue Value()
        {
            _depth++;
            try
            {
                if (_depth > MAX_DEPTH) throw new JsonException("嵌套超过 " + MAX_DEPTH + " 层");
                SkipWs();
                if (Eof) throw new JsonException("意外结束 @" + _i);
                char c = _s[_i];
                switch (c)
                {
                    case '{': return Object();
                    case '[': return Array();
                    case '"': return new JsonValue(JsonValue.Kind.String, false, 0d, Str(), null, null, null);
                    case 't': Word("true"); return new JsonValue(JsonValue.Kind.Bool, true, 1d, null, null, null, null);
                    case 'f': Word("false"); return new JsonValue(JsonValue.Kind.Bool, false, 0d, null, null, null, null);
                    case 'n': Word("null"); return JsonValue.NULL;
                    default: return Number();
                }
            }
            finally { _depth--; }
        }

        private void Word(string w)
        {
            if (_i + w.Length > _s.Length || string.CompareOrdinal(_s, _i, w, 0, w.Length) != 0)
                throw new JsonException("非法字面量 @" + _i);
            _i += w.Length;
        }

        private JsonValue Object()
        {
            _i++;                                                   // {
            Dictionary<string, JsonValue> map = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            List<string> order = new List<string>();
            SkipWs();
            if (!Eof && _s[_i] == '}') { _i++; return Obj(map, order); }
            while (true)
            {
                SkipWs();
                if (Eof || _s[_i] != '"') throw new JsonException("对象键必须是字符串 @" + _i);
                string key = Str();
                SkipWs();
                if (Eof || _s[_i] != ':') throw new JsonException("键后缺冒号 @" + _i);
                _i++;
                JsonValue v = Value();
                if (!map.ContainsKey(key)) order.Add(key);
                map[key] = v;
                SkipWs();
                if (Eof) throw new JsonException("对象未闭合");
                if (_s[_i] == ',') { _i++; continue; }
                if (_s[_i] == '}') { _i++; break; }
                throw new JsonException("对象里意外的字符 " + _s[_i] + " @" + _i);
            }
            return Obj(map, order);
        }

        private static JsonValue Obj(Dictionary<string, JsonValue> map, List<string> order)
        {
            return new JsonValue(JsonValue.Kind.Object, false, 0d, null, null, map, order);
        }

        private JsonValue Array()
        {
            _i++;                                                   // [
            List<JsonValue> list = new List<JsonValue>();
            SkipWs();
            if (!Eof && _s[_i] == ']') { _i++; return Arr(list); }
            while (true)
            {
                list.Add(Value());
                SkipWs();
                if (Eof) throw new JsonException("数组未闭合");
                if (_s[_i] == ',') { _i++; continue; }
                if (_s[_i] == ']') { _i++; break; }
                throw new JsonException("数组里意外的字符 " + _s[_i] + " @" + _i);
            }
            return Arr(list);
        }

        private static JsonValue Arr(List<JsonValue> list)
        {
            return new JsonValue(JsonValue.Kind.Array, false, 0d, null, list, null, null);
        }

        private JsonValue Number()
        {
            int start = _i;
            if (!Eof && (_s[_i] == '-' || _s[_i] == '+')) _i++;
            while (!Eof)
            {
                char c = _s[_i];
                bool ok = (c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '-' || c == '+';
                if (!ok) break;
                _i++;
            }
            string raw = _s.Substring(start, _i - start);
            double d;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new JsonException("非法数字 \"" + raw + "\" @" + start);
            return new JsonValue(JsonValue.Kind.Number, false, d, null, null, null, null);
        }

        private string Str()
        {
            _i++;                                                   // 开引号
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                if (Eof) throw new JsonException("字符串未闭合");
                char c = _s[_i++];
                if (c == '"') break;
                if (c != '\\') { sb.Append(c); continue; }
                if (Eof) throw new JsonException("转义后意外结束");
                char e = _s[_i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (_i + 4 > _s.Length) throw new JsonException("\\u 被截断");
                        string hex = _s.Substring(_i, 4);
                        int code;
                        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            throw new JsonException("非法 \\u" + hex);
                        _i += 4;
                        sb.Append((char)code);
                        break;
                    default: sb.Append(e); break;
                }
            }
            return sb.ToString();
        }
    }
}
