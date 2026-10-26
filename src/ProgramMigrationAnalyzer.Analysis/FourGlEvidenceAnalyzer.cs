using System.Globalization;
using System.Text.RegularExpressions;
using ProgramMigrationAnalyzer.Core;

namespace ProgramMigrationAnalyzer.Analysis;

/// <summary>Bounded symbolic interpretation. Never loads another source or executes any source operation.</summary>
internal sealed class FourGlEvidenceAnalyzer(FourGlEvidence evidence, CancellationToken cancellationToken)
{
    private const int AlternativeLimit = 16;
    private const int CommandAlternativeLimit = 256;
    private const int TextLimit = 8192;
    private const int CallDepthLimit = 8;
    private int _steps;
    private readonly HashSet<string> _globalNames = evidence.Declarations.Where(d => d.Scope == "全域").Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FourGlRoutine> _routines = evidence.Routines.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    private static readonly Regex Identifier = new(@"\b[A-Za-z_][\w]*(?:\.[A-Za-z_][\w]*|\.\*)?", RegexOptions.CultureInvariant);
    private readonly HashSet<string> _commandNames = evidence.Routines.SelectMany(r => FourGlReportNarrator.Nodes(r.Statements))
        .Where(n => n.Kind == "RUN" && n.Expression is not null && Regex.IsMatch(n.Expression, @"^[A-Za-z_]\w*$"))
        .Select(n => n.Expression!).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public void Analyze()
    {
        if (evidence.IsAnalyzed) return;
        var global = new State();
        foreach (var name in _globalNames) global.Values[name] = Unknown(name);
        Execute(evidence.TopLevel, "全域", global, "無額外條件", 0, true);
        foreach (var routine in evidence.Routines)
        {
            var state = global.Copy();
            foreach (var declaration in evidence.Declarations.Where(d => d.Scope == routine.Name)) state.Values[declaration.Name] = Unknown(declaration.Name);
            foreach (var parameter in routine.Parameters) state.Values[parameter] = Unknown(parameter);
            Execute(routine.Statements, routine.Name, state, "無額外條件", 0, true);
        }
        foreach (var sql in evidence.Sql.Where(s => !s.IsCommented))
        {
            var flow = evidence.Flow.FirstOrDefault(f => f.Routine == sql.Routine && f.Span.StartOffset == sql.Span.StartOffset);
            if (flow is not null) sql.Path = flow.Path;
        }
        evidence.IsAnalyzed = true;
    }

    private void Execute(IEnumerable<FourGlStatement> statements, string routine, State state, string path, int depth, bool record)
    {
        foreach (var node in statements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++_steps > 200000)
            {
                if (!evidence.Diagnostics.Any(d => d.Code == "4GL-FLOW-LIMIT")) evidence.Diagnostics.Add(new("4GL-FLOW-LIMIT", "符號分析超過200000步，未展開其餘資料流，語法與原文證據仍保留。", node.Span));
                return;
            }
            if (state.Terminal is not null) break;
            var currentPath = string.IsNullOrWhiteSpace(state.Constraint) ? path : Join(path, state.Constraint);
            if (record)
            {
                evidence.Flow.Add(new(routine, node.Kind, Describe(node), currentPath, node.HeaderSpan));
                TrackSymbols(node, routine);
            }
            if (node.Kind is "IF" or "CASE")
            {
                var outputs = new List<State>();
                foreach (var branch in node.Branches)
                {
                    var copy = state.Copy();
                    copy.Constraint = "";
                    Execute(branch.Statements, routine, copy, Join(currentPath, branch.Condition), depth, record);
                    Guard(copy, branch.Condition);
                    copy.Constraint = JoinGuard(branch.Condition, copy.Constraint);
                    outputs.Add(copy);
                }
                if (node.Kind == "IF" && node.Branches.Count == 1 || node.Kind == "CASE" && !node.Branches.Any(b => b.Condition.StartsWith("前述", StringComparison.Ordinal)))
                {
                    var skipped = state.Copy();
                    var skipCondition = node.Kind == "IF" ? $"NOT ({node.Expression})" : $"CASE {node.Expression} 未匹配";
                    Guard(skipped, skipCondition);
                    skipped.Constraint = skipCondition;
                    outputs.Add(skipped);
                }
                Merge(state, outputs);
                continue;
            }
            if (node.Kind is "FOR" or "WHILE" or "FOREACH")
            {
                var copy = state.Copy();
                if (node.Kind == "FOREACH")
                {
                    var into = Regex.Match(node.Text, @"\bINTO\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (into.Success)
                        foreach (var target in Split(into.Groups[1].Value, ',')) copy.Values[target.Trim()] = Unknown($"游標列 {target.Trim()}");
                }
                Execute(node.Children, routine, copy, Join(currentPath, node.CodeText.Trim()), depth, record);
                if (copy.Terminal is "CONTINUE" or "EXIT") copy.Terminal = null;
                Merge(state, [state.Copy(), copy]);
                continue;
            }
            switch (node.Kind)
            {
                case "LET" when node.Target is not null && node.Expression is not null:
                {
                    var values = Eval(node.Expression, routine, state, currentPath, depth, node, record);
                    state.Values[node.Target] = values;
                    if (record) evidence.Values.Add(new(routine, node.Target, node.Expression, Format(values), currentPath, node.HeaderSpan));
                    break;
                }
                case "CALL" when node.Target is not null:
                {
                    if (record) evidence.Calls.Add(new(routine, node.Target, node.Arguments.ToArray(), node.Returning.ToArray(), currentPath, node.HeaderSpan, _routines.ContainsKey(node.Target)));
                    var args = node.Arguments.Select(a => Eval(a, routine, state, currentPath, depth, node, record)).ToList();
                    if (record)
                        for (var argumentIndex = 0; argumentIndex < args.Count; argumentIndex++)
                            evidence.Values.Add(new(routine, $"{node.Target} 參數{argumentIndex + 1}", $"CALL {node.Target} 參數綁定", Format(args[argumentIndex]), currentPath, node.HeaderSpan));
                    var returned = Invoke(node.Target, args, state, currentPath, depth, node);
                    for (var i = 0; i < node.Returning.Count; i++)
                    {
                        var values = i < returned.Count ? returned[i] : Unknown($"{node.Target} 回傳{i + 1}");
                        state.Values[node.Returning[i]] = values;
                        if (record) evidence.Values.Add(new(routine, node.Returning[i], $"CALL {node.Target} RETURNING", Format(values), currentPath, node.HeaderSpan));
                    }
                    break;
                }
                case "RETURN":
                    var returnedNow = Split(node.Expression ?? "", ',').Select(a => Eval(a, routine, state, currentPath, depth, node, record).Select(v => v with { Guard = JoinGuard(v.Guard, currentPath == "無額外條件" ? "" : currentPath) }).ToList()).ToList();
                    for (var returnIndex = 0; returnIndex < returnedNow.Count; returnIndex++)
                        if (state.Returns.Count > returnIndex) state.Returns[returnIndex] = Bound(state.Returns[returnIndex].Concat(returnedNow[returnIndex]));
                        else state.Returns.Add(returnedNow[returnIndex]);
                    state.Terminal = "RETURN";
                    break;
                case "STOP": state.Terminal = "STOP"; break;
                case "CONTINUE": state.Terminal = "CONTINUE"; break;
                case "EXIT": state.Terminal = "EXIT"; break;
                case "RUN":
                {
                    var expression = node.Expression ?? "";
                    var values = Eval(expression, routine, state, currentPath, depth, node, record);
                    if (record) evidence.Commands.Add(new(routine, expression, Format(values), currentPath, node.HeaderSpan,
                        node.Returning.Count > 0 ? $"原碼有接收狀態至 {string.Join("、", node.Returning)}，是否檢查需依後續條件確認" : "未見此 RUN 接收回傳狀態，不代表外部命令成功"));
                    break;
                }
                case "SELECT":
                {
                    var sql = evidence.Sql.FirstOrDefault(s => !s.IsCommented && s.Span.StartOffset == node.HeaderSpan.StartOffset);
                    if (sql is not null)
                        foreach (var target in sql.Into)
                        {
                            var value = Unknown($"{sql.Id} 查詢結果 {target}");
                            state.Values[target] = value;
                            if (record) evidence.Values.Add(new(routine, target, sql.Id, Format(value), currentPath, node.HeaderSpan));
                        }
                    break;
                }
                case "PREPARE":
                {
                    var match = Regex.Match(node.CodeText, @"^PREPARE\s+(\w+)\s+FROM\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (match.Success)
                    {
                        var values = Eval(match.Groups[2].Value, routine, state, currentPath, depth, node, record);
                        state.Values[$"PREPARE:{match.Groups[1].Value}"] = values;
                        if (record) evidence.Values.Add(new(routine, match.Groups[1].Value, "動態 SQL PREPARE", Format(values), currentPath, node.HeaderSpan));
                    }
                    break;
                }
                case "EXECUTE":
                    if (record)
                    {
                        var key = Regex.Match(node.CodeText, @"^EXECUTE\s+(\w+)", RegexOptions.IgnoreCase).Groups[1].Value;
                        var value = key.Equals("IMMEDIATE", StringComparison.OrdinalIgnoreCase)
                            ? Eval(node.CodeText[17..], routine, state, currentPath, depth, node, record)
                            : state.Values.GetValueOrDefault($"PREPARE:{key}") ?? Unknown(key);
                        evidence.Values.Add(new(routine, key, "動態 SQL EXECUTE，不執行也不假定 schema", Format(value), currentPath, node.HeaderSpan));
                    }
                    break;
            }
        }
    }

    private List<List<Value>> Invoke(string name, List<List<Value>> arguments, State caller, string path, int depth, FourGlStatement node)
    {
        if (!_routines.TryGetValue(name, out var routine)) return [];
        if (depth >= CallDepthLimit)
        {
            if (!evidence.Diagnostics.Any(d => d.Code == "4GL-CALL-LIMIT" && d.Span.StartOffset == node.Span.StartOffset)) evidence.Diagnostics.Add(new("4GL-CALL-LIMIT", $"同檔呼叫 {name} 展開超過8層，回傳值標示未知。", node.HeaderSpan));
            return [];
        }
        var state = new State();
        foreach (var global in _globalNames) state.Values[global] = caller.Values.GetValueOrDefault(global) ?? Unknown(global);
        foreach (var declaration in evidence.Declarations.Where(d => d.Scope == routine.Name)) state.Values[declaration.Name] = Unknown(declaration.Name);
        for (var i = 0; i < routine.Parameters.Count; i++) state.Values[routine.Parameters[i]] = i < arguments.Count ? arguments[i] : Unknown(routine.Parameters[i]);
        Execute(routine.Statements, routine.Name, state, Join(path, $"呼叫 {name}"), depth + 1, false);
        foreach (var global in _globalNames) if (state.Values.TryGetValue(global, out var value)) caller.Values[global] = value;
        return state.Returns;
    }

    private List<Value> Eval(string expression, string routine, State state, string path, int depth, FourGlStatement node, bool record)
    {
        var text = expression.Trim().TrimEnd(';').Trim();
        if (text.Length == 0) return Unknown("空運算式");
        var clipped = Regex.IsMatch(text, @"\s+CLIPPED$", RegexOptions.IgnoreCase);
        if (clipped) text = Regex.Replace(text, @"\s+CLIPPED$", "", RegexOptions.IgnoreCase).Trim();
        var parts = Split(text, ',');
        if (parts.Count > 1)
        {
            var joined = new List<Value> { new("", "", true) };
            foreach (var part in parts)
            {
                var values = Eval(part, routine, state, path, depth, node, record);
                joined = Bound(from left in joined from right in values select new Value(left.Text + right.Text, JoinGuard(left.Guard, right.Guard), left.Known && right.Known));
            }
            return joined;
        }
        if (IsWholeString(text)) return [new(Unquote(text), "", true)];
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out _)) return [new(text, "", true)];
        if (state.Values.TryGetValue(text, out var existing)) return clipped ? existing.Select(v => v with { Text = v.Text.TrimEnd() }).ToList() : existing;
        var call = Regex.Match(text, @"^([A-Za-z_]\w*)\s*\((.*)\)$", RegexOptions.Singleline);
        if (call.Success && _routines.ContainsKey(call.Groups[1].Value))
        {
            var name = call.Groups[1].Value;
            var args = Split(call.Groups[2].Value, ',');
            if (record) evidence.Calls.Add(new(routine, name, args, node.Target is null ? [] : [node.Target], path, node.HeaderSpan, true));
            var returned = Invoke(name, args.Select(a => Eval(a, routine, state, path, depth, node, record)).ToList(), state, path, depth, node);
            return returned.Count > 0 ? returned[0] : Unknown($"{name} 回傳值");
        }
        // Arithmetic remains symbolic unless both operands are literal numeric values.
        var arithmetic = Regex.Match(text, @"^([A-Za-z_]\w*(?:\.\w+)?|\d+(?:\.\d+)?)\s*([+*\-/])\s*([A-Za-z_]\w*(?:\.\w+)?|\d+(?:\.\d+)?)$", RegexOptions.Singleline);
        if (arithmetic.Success)
        {
            var left = Eval(arithmetic.Groups[1].Value, routine, state, path, depth, node, record);
            var right = Eval(arithmetic.Groups[3].Value, routine, state, path, depth, node, record);
            return Bound(from a in left from b in right select Numeric(a, b, arithmetic.Groups[2].Value));
        }
        return Unknown(text);
    }

    private static Value Numeric(Value a, Value b, string operation)
    {
        if (a.Known && b.Known && decimal.TryParse(a.Text, CultureInfo.InvariantCulture, out var x) && decimal.TryParse(b.Text, CultureInfo.InvariantCulture, out var y))
        {
            try
            {
                var number = operation switch { "+" => x + y, "-" => x - y, "*" => x * y, "/" when y != 0 => x / y, _ => throw new ArithmeticException() };
                return new(number.ToString(CultureInfo.InvariantCulture), JoinGuard(a.Guard, b.Guard), true);
            }
            catch (ArithmeticException) { }
        }
        return new($"{{{a.Text} {operation} {b.Text}}}", JoinGuard(a.Guard, b.Guard), false);
    }

    private void Merge(State target, List<State> outputs)
    {
        if (outputs.Count == 0) return;
        var continuing = outputs.Where(s => s.Terminal is null).ToList();
        if (continuing.Count > 0 && continuing.Count != outputs.Count)
            target.Constraint = JoinGuard(target.Constraint, string.Join(" OR ", continuing.Where(s => s.Constraint.Length > 0).Select(s => "(" + s.Constraint + ")").Distinct()));
        var candidates = continuing.Count > 0 ? continuing : outputs;
        foreach (var key in candidates.SelectMany(s => s.Values.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
            target.Values[key] = Bound(candidates.SelectMany(s => s.Values.GetValueOrDefault(key) ?? Unknown(key)), _commandNames.Contains(key) ? CommandAlternativeLimit : AlternativeLimit);
        var maxReturn = outputs.Max(s => s.Returns.Count);
        for (var i = 0; i < maxReturn; i++)
        {
            var index = i;
            var merged = Bound(outputs.Where(s => s.Returns.Count > index).SelectMany(s => s.Returns[index]));
            if (target.Returns.Count > i) target.Returns[i] = Bound(target.Returns[i].Concat(merged));
            else target.Returns.Add(merged);
        }
        target.Terminal = continuing.Count > 0 ? null : outputs.All(s => s.Terminal == outputs[0].Terminal) ? outputs[0].Terminal : "分支終止";
    }

    private static void Guard(State state, string condition)
    {
        foreach (var key in state.Values.Keys.ToList()) state.Values[key] = state.Values[key].Select(v => v with { Guard = JoinGuard(v.Guard, condition) }).ToList();
        state.Returns = state.Returns.Select(values => values.Select(v => v with { Guard = JoinGuard(v.Guard, condition) }).ToList()).ToList();
    }

    private void TrackSymbols(FourGlStatement node, string routine)
    {
        if (node.Kind == "DEFINE") return;
        var text = RemoveStrings(node.CodeText);
        foreach (Match match in Identifier.Matches(text))
        {
            var name = match.Value;
            if (!_globalNames.Contains(name.Split('.')[0]) && !evidence.Declarations.Any(d => d.Scope == routine && d.Name.Equals(name.Split('.')[0], StringComparison.OrdinalIgnoreCase))) continue;
            var sqlInto = node.Kind == "SELECT" && evidence.Sql.Any(s => !s.IsCommented && s.Span.StartOffset == node.HeaderSpan.StartOffset && s.Into.Contains(name, StringComparer.OrdinalIgnoreCase));
            var access = node.Kind == "LET" && string.Equals(node.Target, name, StringComparison.OrdinalIgnoreCase) || node.Returning.Contains(name, StringComparer.OrdinalIgnoreCase) || sqlInto ? "寫入" : "讀取";
            if (!evidence.SymbolUses.Any(u => u.Routine == routine && u.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && u.Access == access && u.Span.StartOffset == node.HeaderSpan.StartOffset)) evidence.SymbolUses.Add(new(routine, name, access, node.HeaderSpan));
        }
    }

    internal static string Describe(FourGlStatement node) => node.Kind switch
    {
        "LET" => $"將 {ReadableExpression(node.Expression ?? "")} 指派給 {node.Target}，保留其後續使用的資料來源",
        "CALL" => $"呼叫 {node.Target}，依序傳入 {string.Join("、", node.Arguments)}{(node.Returning.Count > 0 ? $"，回傳依序接到 {string.Join("、", node.Returning)}" : "，此處未接收回傳值")}",
        "IF" => $"判斷 {ReadableCondition(node.Expression ?? "")}，成立時進第一分支{(node.Branches.Count > 1 ? "，否則進 ELSE 分支" : "，不成立時略過該分支")}",
        "CASE" => $"依 {node.Expression} 選擇符合的 WHEN 分支，保留分支內所有依序執行的語句",
        "FOREACH" => $"逐筆讀取游標資料：{node.Text.Trim()}，每筆進入迴圈內條件與處理",
        "FOR" or "WHILE" => $"依迴圈條件重複處理：{node.Text.Trim()}，靜態分析不假定實際迭代次數",
        "RETURN" => string.IsNullOrWhiteSpace(node.Expression) ? "結束目前處理單元，沒有明示回傳值" : $"回傳 {node.Expression} 並結束目前處理單元",
        "CONTINUE" => "略過本次迴圈剩餘處理，進入下一筆／下一次迭代",
        "STOP" => "結束程式執行，此路徑不再進入後續語句",
        "EXIT" => $"離開指定流程：{node.Text.Trim()}",
        "RUN" => $"交付外部命令 {node.Expression}，命令內容與狀態另列，不假定執行成功",
        "SELECT" => Regex.IsMatch(RemoveStrings(node.Text), @"\bCOUNT\s*\(", RegexOptions.IgnoreCase) ? "計算符合此 SQL 條件的列數／分組數量，將結果供後續條件判斷" : "讀取此 SQL 投影值，供接收變數、游標或輸出使用",
        "UPDATE" => "將符合此 SQL WHERE 的資料列更新為 SET 指定值，狀態意義需結合前置條件判讀",
        "INSERT" => "將 VALUES 或 SELECT 提供的資料新增至目標表",
        "DELETE" => "刪除符合此 SQL WHERE 的資料列",
        "UNLOAD" => $"將緊接的查詢結果輸出至 {node.Text[6..].Trim()}，目的檔案／路徑為原碼設定",
        "DEFINE" => "宣告程式變數與型別，完整宣告另列",
        "DATABASE" => $"設定目前資料庫：{node.Text[8..].Trim()}",
        "WHENEVER" => $"設定後續錯誤處理：{node.Text[8..].Trim()}，需依原碼作用範圍確認",
        "BEGIN" => "開始明示交易",
        "COMMIT" => "提交明示交易",
        "ROLLBACK" => "回復明示交易",
        "INPUT" or "PROMPT" => $"從終端介面取得輸入：{node.Text.Trim()}",
        "DISPLAY" or "PRINT" => $"輸出畫面／報表內容：{node.Text.Trim()}",
        "DECLARE" => $"宣告游標／查詢上下文：{node.Text.Trim()}",
        "PREPARE" => "由字串或變數準備動態 SQL，模板另列，動態值不視為已知",
        "EXECUTE" => "交付已準備或即時的動態 SQL，分析器不執行",
        "UNKNOWN" => "未支援語句，保留完整原文，副作用未知",
        _ => $"原碼明示 {node.Kind} 操作：{node.Text.Trim()}"
    };

    internal static string ReadableCondition(string expression) => Regex.Replace(expression,
        "\"(?:\"\"|\\\\.|[^\"])*\"|'(?:''|\\\\.|[^'])*'|<>|<=|>=|==|=|>|<",
        match => match.Value switch { "<>" => " 不等於 ", "<=" => " 小於或等於 ", ">=" => " 大於或等於 ", "=" or "==" => " 等於 ", ">" => " 大於 ", "<" => " 小於 ", _ => match.Value });
    private static string ReadableExpression(string expression) => Regex.IsMatch(expression, @"\bCURRENT\b", RegexOptions.IgnoreCase) ? $"時間運算結果 {expression}" : expression;
    private static string RemoveStrings(string text) => Regex.Replace(text, "\"(?:\"\"|\\\\.|[^\"])*\"|'(?:''|\\\\.|[^'])*'", " ");
    private static bool IsWholeString(string value)
    {
        if (value.Length < 2 || value[0] is not '\'' and not '"') return false;
        var quote = value[0];
        for (var i = 1; i < value.Length; i++)
        {
            if (value[i] == '\\') { i++; continue; }
            if (value[i] != quote) continue;
            if (i + 1 < value.Length && value[i + 1] == quote) { i++; continue; }
            return i == value.Length - 1;
        }
        return false;
    }
    private static string Unquote(string text) => text[1..^1].Replace(new string(text[0], 2), text[0].ToString());
    internal static List<string> Split(string text, char delimiter)
    {
        var result = new List<string>();
        var start = 0;
        var depth = 0;
        char quote = '\0';
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == '\\') { i++; continue; }
                if (c == quote)
                {
                    if (i + 1 < text.Length && text[i + 1] == quote) { i++; continue; }
                    quote = '\0';
                }
                continue;
            }
            if (c is '\'' or '"') { quote = c; continue; }
            if (c is '(' or '[') depth++;
            if (c is ')' or ']') depth--;
            if (c == delimiter && depth == 0) { result.Add(text[start..i].Trim()); start = i + 1; }
        }
        if (start < text.Length) result.Add(text[start..].Trim());
        return result;
    }
    private static string Join(string path, string branch) => path == "無額外條件" ? branch : $"{path} AND ({branch})";
    private static string JoinGuard(string a, string b) => a.Length == 0 ? b : b.Length == 0 || a == b ? a : $"{a} AND {b}";
    private static List<Value> Unknown(string expression) => [new($"{{{expression}：未知}}", "", false)];
    private static List<Value> Bound(IEnumerable<Value> values, int limit = AlternativeLimit)
    {
        var list = values.Distinct().Take(limit + 1).ToList();
        if (list.Count > limit || list.Any(v => v.Text.Length > TextLimit || v.Guard.Length > TextLimit)) return Unknown("候選值、字串或條件長度超過有界分析上限");
        return list;
    }
    private static string Format(List<Value> values) => string.Join(" 或 ", values.Select(v => v.Text + (v.Guard.Length > 0 ? $"〔{v.Guard}〕" : "")));
    private sealed record Value(string Text, string Guard, bool Known);
    private sealed class State
    {
        public Dictionary<string, List<Value>> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<List<Value>> Returns { get; set; } = [];
        public string? Terminal { get; set; }
        public string Constraint { get; set; } = "";
        public State Copy()
        {
            var copy = new State { Returns = Returns.Select(v => v.ToList()).ToList(), Terminal = Terminal, Constraint = Constraint };
            foreach (var pair in Values) copy.Values[pair.Key] = pair.Value.ToList();
            return copy;
        }
    }
}
