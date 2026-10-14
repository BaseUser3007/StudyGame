using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Перед запуском вставляет в код вызовы списания энергии, чтобы платить за то,
/// что реально выполняется, а не за текст: цикл на 100 итераций стоит в 100 раз дороже.
///
///   if / elseif / while / until  — при каждой проверке условия:  if __energy(3) and cond then
///   for                          — на каждой итерации:            for ... do __energy(5) ...
///   function                     — при каждом вызове:             function f(a) __energy(2) ...
///   else                         — при каждом входе в ветку
///   local / return               — при каждом выполнении:         __energy(1) local x = ...
///
/// `__energy(n) and cond` всегда равно cond по истинности (__energy возвращает true),
/// а `and` слабее всех операторов, кроме `or`, поэтому скобки вокруг условия не нужны.
/// Переводы строк не добавляются — номера строк в ошибках остаются прежними.
/// Стоимость команд списывается отдельно, при вызове (LuaCommandBase).
/// </summary>
public class EnergyInstrumenter
{
    public const string ChargeFunction = "__energy";

    private readonly LuaTokenizer _tokenizer = new LuaTokenizer();
    private readonly EnergyConfig _config;

    public EnergyInstrumenter(EnergyConfig config)
    {
        _config = config;
    }

    public string Instrument(string code)
    {
        if (string.IsNullOrEmpty(code)) return code;

        List<LuaTokenizer.Token> tokens = _tokenizer.Tokenize(code);
        if (!LuaTokenizer.Reassembles(tokens, code))
        {
            // Токенизатор не смог разобрать код целиком — запускаем как есть, чтобы не сломать его
            Debug.LogWarning("EnergyInstrumenter: code could not be tokenized exactly, running without energy metering");
            return code;
        }

        var sb = new StringBuilder(code.Length + tokens.Count);
        int pendingForDo = 0;
        bool pendingFunctionParams = false;

        foreach (LuaTokenizer.Token token in tokens)
        {
            if (token.Type == LuaTokenizer.TokenType.Keyword)
            {
                switch (token.Value)
                {
                    case "if":     AppendCondition(sb, token.Value, _config.ifCost); continue;
                    case "elseif": AppendCondition(sb, token.Value, _config.elseifCost); continue;
                    case "while":  AppendCondition(sb, token.Value, _config.whileCost); continue;
                    // Стоимость repeat берётся при каждой проверке until
                    case "until":  AppendCondition(sb, token.Value, _config.repeatCost); continue;

                    case "else":
                        sb.Append(token.Value);
                        AppendCharge(sb, _config.elseCost, leadingSpace: true);
                        continue;

                    case "local":
                    case "return":
                        float cost = token.Value == "local" ? _config.localCost : _config.returnCost;
                        AppendCharge(sb, cost, leadingSpace: false);
                        sb.Append(token.Value);
                        continue;

                    case "for":
                        // Заголовок for не содержит do — первый do после for принадлежит ему
                        pendingForDo++;
                        break;

                    case "do":
                        if (pendingForDo > 0)
                        {
                            pendingForDo--;
                            sb.Append(token.Value);
                            AppendCharge(sb, _config.forCost, leadingSpace: true);
                            continue;
                        }
                        break;

                    case "function":
                        pendingFunctionParams = true;
                        break;
                }
            }
            else if (pendingFunctionParams && token.Type == LuaTokenizer.TokenType.Operator && token.Value == ")")
            {
                // Конец списка параметров — начало тела функции
                pendingFunctionParams = false;
                sb.Append(token.Value);
                AppendCharge(sb, _config.functionCost, leadingSpace: true);
                continue;
            }

            sb.Append(token.Value);
        }

        return sb.ToString();
    }

    // ─── Helpers ──────────────────────────────────────────────

    private static void AppendCondition(StringBuilder sb, string keyword, float cost)
    {
        sb.Append(keyword);
        if (cost <= 0f) return;
        sb.Append(' ').Append(ChargeFunction).Append('(').Append(Format(cost)).Append(") and");
    }

    /// <summary>
    /// Отдельный оператор `;__energy(n);` — точки с запятой исключают склейку с соседними выражениями.
    /// </summary>
    private static void AppendCharge(StringBuilder sb, float cost, bool leadingSpace)
    {
        if (cost <= 0f) return;
        if (leadingSpace) sb.Append(' ');
        sb.Append(';').Append(ChargeFunction).Append('(').Append(Format(cost)).Append(");");
        if (!leadingSpace) sb.Append(' ');
    }

    private static string Format(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
