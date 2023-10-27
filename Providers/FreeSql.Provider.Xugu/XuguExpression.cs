﻿using FreeSql.Internal;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;

namespace FreeSql.Xugu
{
    class XuguExpression : CommonExpression
    {

        public XuguExpression(CommonUtils common) : base(common) { }

        public override string ExpressionLambdaToSqlOther(Expression exp, ExpTSC tsc)
        {
            Func<Expression, string> getExp = exparg => ExpressionLambdaToSql(exparg, tsc);
            switch (exp.NodeType)
            {
                case ExpressionType.ArrayLength:
                    var arrOper = (exp as UnaryExpression)?.Operand;
                    var arrOperExp = getExp(arrOper);
                    if (arrOperExp.StartsWith("(") || arrOperExp.EndsWith(")")) return $"array_length(array[{arrOperExp.TrimStart('(').TrimEnd(')')}],1)";
                    if (arrOper.Type == typeof(byte[])) return $"octet_length({getExp(arrOper)})";
                    return $"case when {arrOperExp} is null then 0 else array_length({arrOperExp},1) end";
                case ExpressionType.Convert:
                    var operandExp = (exp as UnaryExpression)?.Operand;
                    var gentype = exp.Type.NullableTypeOrThis();
                    if (gentype != operandExp.Type.NullableTypeOrThis())
                    {
                        switch (exp.Type.NullableTypeOrThis().ToString())
                        {
                            case "System.Boolean": return $"(cast({getExp(operandExp)} as varchar) not in ('0','false'))";
                            case "System.Byte": return $"cast({getExp(operandExp)} as tinyint)";
                            case "System.Char": return $"substring(cast({getExp(operandExp)} as varchar),1,1)";
                            case "System.DateTime": return ExpressionConstDateTime(operandExp) ?? $"cast({getExp(operandExp)} as datetime)";
                            case "System.Decimal": return $"cast({getExp(operandExp)} as numeric(36,18))";
                            case "System.Double": return $"cast({getExp(operandExp)} as numeric(32,16))";
                            case "System.Int16": return $"cast({getExp(operandExp)} as smallint)";
                            case "System.Int32": return $"cast({getExp(operandExp)} as int)";
                            case "System.Int64": return $"cast({getExp(operandExp)} as bigint)";
                            case "System.SByte": return $"cast({getExp(operandExp)} as tinyint)";
                            case "System.Single": return $"cast({getExp(operandExp)} as numeric(14,7))";
                            case "System.String":
                                return gentype == typeof(Guid) ?
                                    $"cast({getExp(operandExp)} as char(36))" :
                                    $"cast({getExp(operandExp)} as {(gentype.IsNumberType() || gentype.IsEnum ? "varchar(100)" : "text")})";
                            case "System.UInt16": return $"cast({getExp(operandExp)} as smallint)";
                            case "System.UInt32": return $"cast({getExp(operandExp)} as int)";
                            case "System.UInt64": return $"cast({getExp(operandExp)} as numeric(20,0))";
                            case "System.Guid": return $"cast({getExp(operandExp)} as char(36))";
                        }
                    }
                    break;
                case ExpressionType.Call:
                    var callExp = exp as MethodCallExpression;

                    switch (callExp.Method.Name)
                    {
                        case "Parse":
                        case "TryParse":
                            switch (callExp.Method.DeclaringType.NullableTypeOrThis().ToString())
                            {
                                case "System.Boolean": return $"(cast({getExp(callExp.Arguments[0])} as varchar) not in ('0','false'))";
                                case "System.Byte": return $"cast({getExp(callExp.Arguments[0])} as tinyint)";
                                case "System.Char": return $"substring(cast({getExp(callExp.Arguments[0])} as varchar),1,1)";
                                case "System.DateTime": return ExpressionConstDateTime(callExp.Arguments[0]) ?? $"cast({getExp(callExp.Arguments[0])} as datetime)";
                                case "System.Decimal": return $"cast({getExp(callExp.Arguments[0])} as numeric(36,18))";
                                case "System.Double": return $"cast({getExp(callExp.Arguments[0])} as numeric(32,16))";
                                case "System.Int16": return $"cast({getExp(callExp.Arguments[0])} as smallint)";
                                case "System.Int32": return $"cast({getExp(callExp.Arguments[0])} as int)";
                                case "System.Int64": return $"cast({getExp(callExp.Arguments[0])} as bigint)";
                                case "System.SByte": return $"cast({getExp(callExp.Arguments[0])} as tinyint)";
                                case "System.Single": return $"cast({getExp(callExp.Arguments[0])} as numeric(14,7))";
                                case "System.UInt16": return $"cast({getExp(callExp.Arguments[0])} as smallint)";
                                case "System.UInt32": return $"cast({getExp(callExp.Arguments[0])} as int)";
                                case "System.UInt64": return $"cast({getExp(callExp.Arguments[0])} as numeric(20,0))";
                                case "System.Guid": return $"cast({getExp(callExp.Arguments[0])} as char(36))";
                            }
                            break;
                        case "NewGuid":
                            return null;
                        case "Next":
                            if (callExp.Object?.Type == typeof(Random)) return "cast(random()*1000000000 as int)";
                            return null;
                        case "NextDouble":
                            if (callExp.Object?.Type == typeof(Random)) return "random()";
                            return null;
                        case "Random":
                            if (callExp.Method.DeclaringType.IsNumberType()) return "random()";
                            return null;
                        case "ToString":
                            if (callExp.Object != null)
                            {
                                if (callExp.Object.Type.NullableTypeOrThis().IsEnum)
                                {
                                    tsc.SetMapColumnTmp(null);
                                    var oldMapType = tsc.SetMapTypeReturnOld(typeof(string));
                                    var enumStr = ExpressionLambdaToSql(callExp.Object, tsc);
                                    tsc.SetMapColumnTmp(null).SetMapTypeReturnOld(oldMapType);
                                    return enumStr;
                                }
                                return callExp.Arguments.Count == 0 ? $"cast({getExp(callExp.Object)} as text)" : null;
                            }
                            return null;
                    }

                    var objExp = callExp.Object;
                    var objType = objExp?.Type;
                    if (objType?.FullName == "System.Byte[]") return null;

                    var argIndex = 0;
                    if (objType == null && callExp.Method.DeclaringType == typeof(Enumerable))
                    {
                        objExp = callExp.Arguments.FirstOrDefault();
                        objType = objExp?.Type;
                        argIndex++;

                        if (objType == typeof(string))
                        {
                            switch (callExp.Method.Name)
                            {
                                case "First":
                                case "FirstOrDefault":
                                    return $"substr({getExp(callExp.Arguments[0])}, 1, 1)";
                            }
                        }
                    }
                    if (objType == null) objType = callExp.Method.DeclaringType;
                    if (objType != null || objType.IsArrayOrList())
                    {
                        if (argIndex >= callExp.Arguments.Count) break;
                        tsc.SetMapColumnTmp(null);
                        var args1 = getExp(callExp.Arguments[argIndex]);
                        var oldMapType = tsc.SetMapTypeReturnOld(tsc.mapTypeTmp);
                        var oldDbParams = objExp?.NodeType == ExpressionType.MemberAccess ? tsc.SetDbParamsReturnOld(null) : null; //#900 UseGenerateCommandParameterWithLambda(true) 子查询 bug、以及 #1173 参数化 bug
                        tsc.isNotSetMapColumnTmp = true;
                        var left = objExp == null ? null : getExp(objExp);
                        tsc.isNotSetMapColumnTmp = false;
                        tsc.SetMapColumnTmp(null).SetMapTypeReturnOld(oldMapType);
                        if (oldDbParams != null) tsc.SetDbParamsReturnOld(oldDbParams);
                        switch (callExp.Method.Name)
                        {
                            case "Contains":
                                //判断 in //在各大 Provider AdoProvider 中已约定，500元素分割, 3空格\r\n4空格
                                return $"(({args1}) in {left.Replace(",   \r\n    \r\n", $") \r\n OR ({args1}) in (")})";
                        }
                    }
                    break;
                case ExpressionType.MemberAccess:
                    var memExp = exp as MemberExpression;
                    var memParentExp = memExp.Expression?.Type;
                    if (memParentExp?.FullName == "System.Byte[]") return null;
                    if (memParentExp != null)
                    {
                        if (memParentExp.IsArray == true)
                        {
                            var left = getExp(memExp.Expression);
                            if (left.StartsWith("(") || left.EndsWith(")")) left = $"array[{left.TrimStart('(').TrimEnd(')')}]";
                            switch (memExp.Member.Name)
                            {
                                case "Length":
                                case "Count": return $"case when {left} is null then 0 else array_length({left},1) end";
                            }
                        }
                        switch (memParentExp.FullName)
                        {
                            case "Newtonsoft.Json.Linq.JToken":
                            case "Newtonsoft.Json.Linq.JObject":
                            case "Newtonsoft.Json.Linq.JArray":
                                var left = getExp(memExp.Expression);
                                switch (memExp.Member.Name)
                                {
                                    case "Count": return $"jsonb_array_length(coalesce({left},'[]'))";
                                }
                                break;
                        }
                        if (memParentExp == typeof(Dictionary<string, string>))
                        {
                            var left = getExp(memExp.Expression);
                            switch (memExp.Member.Name)
                            {
                                case "Count": return $"case when {left} is null then 0 else array_length(akeys({left}),1) end";
                                case "Keys": return $"akeys({left})";
                                case "Values": return $"avals({left})";
                            }
                        }
                    }
                    break;
                case ExpressionType.NewArrayInit:
                    var arrExp = exp as NewArrayExpression;
                    var arrSb = new StringBuilder();
                    arrSb.Append("array[");
                    for (var a = 0; a < arrExp.Expressions.Count; a++)
                    {
                        if (a > 0) arrSb.Append(",");
                        arrSb.Append(getExp(arrExp.Expressions[a]));
                    }
                    if (arrSb.Length == 1) arrSb.Append("NULL");
                    return arrSb.Append("]").ToString();
                case ExpressionType.ListInit:
                    var listExp = exp as ListInitExpression;
                    var listSb = new StringBuilder();
                    listSb.Append("(");
                    for (var a = 0; a < listExp.Initializers.Count; a++)
                    {
                        if (listExp.Initializers[a].Arguments.Any() == false) continue;
                        if (a > 0) listSb.Append(",");
                        listSb.Append(getExp(listExp.Initializers[a].Arguments.FirstOrDefault()));
                    }
                    if (listSb.Length == 1) listSb.Append("NULL");
                    return listSb.Append(")").ToString();
                case ExpressionType.New:
                    var newExp = exp as NewExpression;
                    if (typeof(IList).IsAssignableFrom(newExp.Type))
                    {
                        if (newExp.Arguments.Count == 0) return "(NULL)";
                        if (typeof(IEnumerable).IsAssignableFrom(newExp.Arguments[0].Type) == false) return "(NULL)";
                        return getExp(newExp.Arguments[0]);
                    }
                    return null;
            }
            return null;
        }

        public override string ExpressionLambdaToSqlMemberAccessString(MemberExpression exp, ExpTSC tsc)
        {
            if (exp.Expression == null)
            {
                switch (exp.Member.Name)
                {
                    case "Empty": return "''";
                }
                return null;
            }
            var left = ExpressionLambdaToSql(exp.Expression, tsc);
            switch (exp.Member.Name)
            {
                case "Length": return $"char_length({left})";
            }
            return null;
        }
        public override string ExpressionLambdaToSqlMemberAccessDateTime(MemberExpression exp, ExpTSC tsc)
        {
            if (exp.Expression == null)
            {
                switch (exp.Member.Name)
                {
                    case "Now": return _common.Now;
                    case "UtcNow": return _common.NowUtc;
                    case "Today": return "convert(char(10),getdate(),120)";
                    case "MinValue": return "'1753/1/1 0:00:00'";
                    case "MaxValue": return "'9999/12/31 23:59:59'";
                }
                return null;
            }
            var left = ExpressionLambdaToSql(exp.Expression, tsc);
            switch (exp.Member.Name)
            {
                case "Date": return $"convert(char(10),{left},120)";
                case "TimeOfDay": return $"datediff(second, convert(char(10),{left},120), {left})";
                case "DayOfWeek": return $"(datepart(weekday, {left})-1)";
                case "Day": return $"datepart(day, {left})";
                case "DayOfYear": return $"datepart(dayofyear, {left})";
                case "Month": return $"datepart(month, {left})";
                case "Year": return $"datepart(year, {left})";
                case "Hour": return $"datepart(hour, {left})";
                case "Minute": return $"datepart(minute, {left})";
                case "Second": return $"datepart(second, {left})";
                case "Millisecond": return $"(datepart(millisecond, {left})/1000)";
                case "Ticks": return $"(cast(datediff(second, '1970-1-1', {left}) as bigint)*10000000+621355968000000000)";
            }
            return null;
        }
        public override string ExpressionLambdaToSqlMemberAccessTimeSpan(MemberExpression exp, ExpTSC tsc)
        {
            if (exp.Expression == null)
            {
                switch (exp.Member.Name)
                {
                    case "Zero": return "0";
                    case "MinValue": return "-922337203685477580"; //微秒 Ticks / 10
                    case "MaxValue": return "922337203685477580";
                }
                return null;
            }
            var left = ExpressionLambdaToSql(exp.Expression, tsc);
            switch (exp.Member.Name)
            {
                case "Days": return $"floor(({left})/{60 * 60 * 24})";
                case "Hours": return $"floor(({left})/{60 * 60}%24)";
                case "Milliseconds": return $"(cast({left} as bigint)*1000)";
                case "Minutes": return $"floor(({left})/60%60)";
                case "Seconds": return $"(({left})%60)";
                case "Ticks": return $"(cast({left} as bigint)*10000000)";
                case "TotalDays": return $"(({left})/{60 * 60 * 24})";
                case "TotalHours": return $"(({left})/{60 * 60})";
                case "TotalMilliseconds": return $"(cast({left} as bigint)*1000)";
                case "TotalMinutes": return $"(({left})/60)";
                case "TotalSeconds": return $"({left})";
            }
            return null;
        }

        public override string ExpressionLambdaToSqlCallString(MethodCallExpression exp, ExpTSC tsc)
        {
            Func<Expression, string> getExp = exparg => ExpressionLambdaToSql(exparg, tsc);
            if (exp.Object == null)
            {
                switch (exp.Method.Name)
                {
                    case "IsNullOrEmpty":
                        var arg1 = getExp(exp.Arguments[0]);
                        return $"({arg1} is null or {arg1} = '')";
                    case "IsNullOrWhiteSpace":
                        var arg2 = getExp(exp.Arguments[0]);
                        return $"({arg2} is null or {arg2} = '' or ltrim({arg2}) = '')";
                    case "Concat":
                        return _common.StringConcat(exp.Arguments.Select(a => getExp(a)).ToArray(), null);
                    case "Format":
                        if (exp.Arguments[0].NodeType != ExpressionType.Constant) throw new Exception(CoreStrings.Not_Implemented_Expression_ParameterUseConstant(exp,exp.Arguments[0]));
                        var expArgsHack = exp.Arguments.Count == 2 && exp.Arguments[1].NodeType == ExpressionType.NewArrayInit ?
                            (exp.Arguments[1] as NewArrayExpression).Expressions : exp.Arguments.Where((a, z) => z > 0);
                        //3个 {} 时，Arguments 解析出来是分开的
                        //4个 {} 时，Arguments[1] 只能解析这个出来，然后里面是 NewArray []
                        var expArgs = expArgsHack.Select(a =>
                        {
                            var atype = (a as UnaryExpression)?.Operand.Type.NullableTypeOrThis() ?? a.Type.NullableTypeOrThis();
                            if (atype == typeof(string)) return $"'||{_common.IsNull(ExpressionLambdaToSql(a, tsc), "''")}||'";
                            return $"'||{_common.IsNull($"cast({ExpressionLambdaToSql(a, tsc)} text)", "''")}||'";
                        }).ToArray();
                        return string.Format(ExpressionLambdaToSql(exp.Arguments[0], tsc), expArgs);
                    case "Join":
                        if (exp.IsStringJoin(out var tolistObjectExp, out var toListMethod, out var toListArgs1))
                        {
                            var newToListArgs0 = Expression.Call(tolistObjectExp, toListMethod,
                                Expression.Lambda(
                                    Expression.Call(
                                        typeof(SqlExtExtensions).GetMethod("StringJoinPgsqlGroupConcat"),
                                        Expression.Convert(toListArgs1.Body, typeof(object)),
                                        Expression.Convert(exp.Arguments[0], typeof(object))),
                                    toListArgs1.Parameters));
                            var newToListSql = getExp(newToListArgs0);
                            return newToListSql;
                        }
                        break;
                }
            }
            else
            {
                var left = getExp(exp.Object);
                switch (exp.Method.Name)
                {
                    case "StartsWith":
                    case "EndsWith":
                    case "Contains":
                        var args0Value = getExp(exp.Arguments[0]);
                        if (args0Value == "NULL") return $"({left}) IS NULL";
                        if (args0Value.Contains("%"))
                        {
                            if (exp.Method.Name == "StartsWith") return $"strpos({args0Value}, {left}) = 1";
                            if (exp.Method.Name == "EndsWith") return $"strpos({args0Value}, {left}) = char_length({args0Value})";
                            return $"strpos({args0Value}, {left}) > 0";
                        }
                        var likeOpt = "LIKE";
                        if (exp.Arguments.Count > 1)
                        {
                            if (exp.Arguments[1].Type == typeof(bool) ||
                                exp.Arguments[1].Type == typeof(StringComparison)) likeOpt = "ILIKE";
                        }
                        if (exp.Method.Name == "StartsWith") return $"({left}) {likeOpt} {(args0Value.EndsWith("'") ? args0Value.Insert(args0Value.Length - 1, "%") : $"(cast({args0Value} as text) || '%')")}";
                        if (exp.Method.Name == "EndsWith") return $"({left}) {likeOpt} {(args0Value.StartsWith("'") ? args0Value.Insert(1, "%") : $"('%' || cast({args0Value} as text))")}";
                        if (args0Value.StartsWith("'") && args0Value.EndsWith("'")) return $"({left}) {likeOpt} {args0Value.Insert(1, "%").Insert(args0Value.Length, "%")}";
                        return $"({left}) {likeOpt} ('%' || cast({args0Value} as text) || '%')";
                    case "ToLower": return $"lower({left})";
                    case "ToUpper": return $"upper({left})";
                    case "Substring":
                        var substrArgs1 = getExp(exp.Arguments[0]);
                        if (long.TryParse(substrArgs1, out var testtrylng1)) substrArgs1 = (testtrylng1 + 1).ToString();
                        else substrArgs1 += "+1";
                        if (exp.Arguments.Count == 1) return $"substr({left}, {substrArgs1})";
                        return $"substr({left}, {substrArgs1}, {getExp(exp.Arguments[1])})";
                    case "IndexOf": return $"(strpos({left}, {getExp(exp.Arguments[0])})-1)";
                    case "PadLeft":
                        if (exp.Arguments.Count == 1) return $"lpad({left}, {getExp(exp.Arguments[0])})";
                        return $"lpad({left}, {getExp(exp.Arguments[0])}, {getExp(exp.Arguments[1])})";
                    case "PadRight":
                        if (exp.Arguments.Count == 1) return $"rpad({left}, {getExp(exp.Arguments[0])})";
                        return $"rpad({left}, {getExp(exp.Arguments[0])}, {getExp(exp.Arguments[1])})";
                    case "Trim":
                    case "TrimStart":
                    case "TrimEnd":
                        if (exp.Arguments.Count == 0)
                        {
                            if (exp.Method.Name == "Trim") return $"trim({left})";
                            if (exp.Method.Name == "TrimStart") return $"ltrim({left})";
                            if (exp.Method.Name == "TrimEnd") return $"rtrim({left})";
                        }
                        var trimArg1 = "";
                        var trimArg2 = "";
                        foreach (var argsTrim02 in exp.Arguments)
                        {
                            var argsTrim01s = new[] { argsTrim02 };
                            if (argsTrim02.NodeType == ExpressionType.NewArrayInit)
                            {
                                var arritem = argsTrim02 as NewArrayExpression;
                                argsTrim01s = arritem.Expressions.ToArray();
                            }
                            foreach (var argsTrim01 in argsTrim01s)
                            {
                                var trimChr = getExp(argsTrim01).Trim('\'');
                                if (trimChr.Length == 1) trimArg1 += trimChr;
                                else trimArg2 += $" || ({trimChr})";
                            }
                        }
                        if (exp.Method.Name == "Trim") left = $"trim({left}, {_common.FormatSql("{0}", trimArg1)}{trimArg2})";
                        if (exp.Method.Name == "TrimStart") left = $"ltrim({left}, {_common.FormatSql("{0}", trimArg1)}{trimArg2})";
                        if (exp.Method.Name == "TrimEnd") left = $"rtrim({left}, {_common.FormatSql("{0}", trimArg1)}{trimArg2})";
                        return left;
                    case "Replace": return $"replace({left}, {getExp(exp.Arguments[0])}, {getExp(exp.Arguments[1])})";
                    case "CompareTo": return $"case when {left} = {getExp(exp.Arguments[0])} then 0 when {left} > {getExp(exp.Arguments[0])} then 1 else -1 end";
                    case "Equals": return $"({left} = cast({getExp(exp.Arguments[0])} text))";
                }
            }
            return null;
        }
        public override string ExpressionLambdaToSqlCallMath(MethodCallExpression exp, ExpTSC tsc)
        {
            Func<Expression, string> getExp = exparg => ExpressionLambdaToSql(exparg, tsc);
            switch (exp.Method.Name)
            {
                case "Abs": return $"abs({getExp(exp.Arguments[0])})";
                case "Sign": return $"sign({getExp(exp.Arguments[0])})";
                case "Floor": return $"floor({getExp(exp.Arguments[0])})";
                case "Ceiling": return $"ceiling({getExp(exp.Arguments[0])})";
                case "Round":
                    if (exp.Arguments.Count > 1 && exp.Arguments[1].Type.FullName == "System.Int32") return $"round({getExp(exp.Arguments[0])}, {getExp(exp.Arguments[1])})";
                    return $"round({getExp(exp.Arguments[0])})";
                case "Exp": return $"exp({getExp(exp.Arguments[0])})";
                case "Log": return $"log({getExp(exp.Arguments[0])})";
                case "Log10": return $"log10({getExp(exp.Arguments[0])})";
                case "Pow": return $"pow({getExp(exp.Arguments[0])}, {getExp(exp.Arguments[1])})";
                case "Sqrt": return $"sqrt({getExp(exp.Arguments[0])})";
                case "Cos": return $"cos({getExp(exp.Arguments[0])})";
                case "Sin": return $"sin({getExp(exp.Arguments[0])})";
                case "Tan": return $"tan({getExp(exp.Arguments[0])})";
                case "Acos": return $"acos({getExp(exp.Arguments[0])})";
                case "Asin": return $"asin({getExp(exp.Arguments[0])})";
                case "Atan": return $"atan({getExp(exp.Arguments[0])})";
                case "Atan2": return $"atan2({getExp(exp.Arguments[0])}, {getExp(exp.Arguments[1])})";
                case "Truncate": return $"trunc({getExp(exp.Arguments[0])}, 0)";
            }
            return null;
        }
        public override string ExpressionLambdaToSqlCallDateTime(MethodCallExpression exp, ExpTSC tsc)
        {
            Func<Expression, string> getExp = exparg => ExpressionLambdaToSql(exparg, tsc);
            if (exp.Object == null)
            {
                switch (exp.Method.Name)
                {
                    case "Compare": return $"({getExp(exp.Arguments[0])} - ({getExp(exp.Arguments[1])}))";
                    case "DaysInMonth": return $"datepart(day, dateadd(day, -1, dateadd(month, 1, cast({getExp(exp.Arguments[0])} as varchar(100)) + '-' + cast({getExp(exp.Arguments[1])} as varchar(100)) + '-1')))";
                    case "Equals": return $"({getExp(exp.Arguments[0])} = {getExp(exp.Arguments[1])})";

                    case "IsLeapYear":
                        var isLeapYearArgs1 = getExp(exp.Arguments[0]);
                        return $"(({isLeapYearArgs1})%4=0 AND ({isLeapYearArgs1})%100<>0 OR ({isLeapYearArgs1})%400=0)";

                    case "Parse": return ExpressionConstDateTime(exp.Arguments[0]) ?? $"cast({getExp(exp.Arguments[0])} as datetime)";
                    case "ParseExact":
                    case "TryParse":
                    case "TryParseExact": return ExpressionConstDateTime(exp.Arguments[0]) ?? $"cast({getExp(exp.Arguments[0])} as datetime)";
                }
            }
            else
            {
                var left = getExp(exp.Object);
                var args1 = exp.Arguments.Count == 0 ? null : getExp(exp.Arguments[0]);
                switch (exp.Method.Name)
                {
                    case "Add": return $"dateadd(second, {args1}, {left})";
                    case "AddDays": return $"dateadd(day, {args1}, {left})";
                    case "AddHours": return $"dateadd(hour, {args1}, {left})";
                    case "AddMilliseconds": return $"dateadd(second, ({args1})/1000, {left})";
                    case "AddMinutes": return $"dateadd(minute, {args1}, {left})";
                    case "AddMonths": return $"dateadd(month, {args1}, {left})";
                    case "AddSeconds": return $"dateadd(second, {args1}, {left})";
                    case "AddTicks": return $"dateadd(second, ({args1})/10000000, {left})";
                    case "AddYears": return $"dateadd(year, {args1}, {left})";
                    case "Subtract":
                        switch ((exp.Arguments[0].Type.IsNullableType() ? exp.Arguments[0].Type.GetGenericArguments().FirstOrDefault() : exp.Arguments[0].Type).FullName)
                        {
                            case "System.DateTime": return $"datediff(second, {args1}, {left})";
                            case "System.TimeSpan": return $"dateadd(second, ({args1})*-1, {left})";
                        }
                        break;
                    case "Equals": return $"({left} = {args1})";
                    case "CompareTo": return $"datediff(second,{args1},{left})";
                    case "ToString":
                        if (left.EndsWith(" as datetime)") == false) left = $"cast({left} as datetime)";
                        if (exp.Arguments.Count == 0) return $"convert(varchar, {left}, 121)";
                        switch (args1.TrimStart('N'))
                        {
                            case "'yyyy-MM-dd HH:mm:ss'": return $"convert(char(19), {left}, 120)";
                            case "'yyyy-MM-dd HH:mm'": return $"substring(convert(char(19), {left}, 120), 1, 16)";
                            case "'yyyy-MM-dd HH'": return $"substring(convert(char(19), {left}, 120), 1, 13)";
                            case "'yyyy-MM-dd'": return $"convert(char(10), {left}, 23)";
                            case "'yyyy-MM'": return $"substring(convert(char(10), {left}, 23), 1, 7)";
                            case "'yyyyMMdd'": return $"convert(char(8), {left}, 112)";
                            case "'yyyyMM'": return $"substring(convert(char(8), {left}, 112), 1, 6)";
                            case "'yyyy'": return $"substring(convert(char(8), {left}, 112), 1, 4)";
                            case "'HH:mm:ss'": return $"convert(char(8), {left}, 24)";
                        }
                        var isMatched = false;
                        var nchar = args1.StartsWith("N'") ? "N" : "";
                        args1 = Regex.Replace(args1, "(yyyy|yy|MM|M|dd|d|HH|H|hh|h|mm|m|ss|s|tt|t)", m =>
                        {
                            isMatched = true;
                            switch (m.Groups[1].Value)
                            {
                                case "yyyy": return $"' + substring(convert(char(8), {left}, 112), 1, 4) + {nchar}'";
                                case "yy": return $"' + substring(convert(char(6), {left}, 12), 1, 2) + {nchar}'";
                                case "MM": return $"' + substring(convert(char(6), {left}, 12), 3, 2) + {nchar}'";
                                case "M": return $"' + case when substring(convert(char(6), {left}, 12), 3, 1) = '0' then substring(convert(char(6), {left}, 12), 4, 1) else substring(convert(char(6), {left}, 12), 3, 2) end + {nchar}'";
                                case "dd": return $"' + substring(convert(char(6), {left}, 12), 5, 2) + {nchar}'";
                                case "d": return $"' + case when substring(convert(char(6), {left}, 12), 5, 1) = '0' then substring(convert(char(6), {left}, 12), 6, 1) else substring(convert(char(6), {left}, 12), 5, 2) end + {nchar}'";
                                case "HH": return $"' + substring(convert(char(8), {left}, 24), 1, 2) + {nchar}'";
                                case "H": return $"' + case when substring(convert(char(8), {left}, 24), 1, 1) = '0' then substring(convert(char(8), {left}, 24), 2, 1) else substring(convert(char(8), {left}, 24), 1, 2) end + {nchar}'";
                                case "hh":
                                    return $"' + case cast(case when substring(convert(char(8), {left}, 24), 1, 1) = '0' then substring(convert(char(8), {left}, 24), 2, 1) else substring(convert(char(8), {left}, 24), 1, 2) end as int) % 12" +
                                    $"when 0 then '12' when 1 then '01' when 2 then '02' when 3 then '03' when 4 then '04' when 5 then '05' when 6 then '06' when 7 then '07' when 8 then '08' when 9 then '09' when 10 then '10' when 11 then '11' end + {nchar}'";
                                case "h":
                                    return $"' + case cast(case when substring(convert(char(8), {left}, 24), 1, 1) = '0' then substring(convert(char(8), {left}, 24), 2, 1) else substring(convert(char(8), {left}, 24), 1, 2) end as int) % 12" +
                                    $"when 0 then '12' when 1 then '1' when 2 then '2' when 3 then '3' when 4 then '4' when 5 then '5' when 6 then '6' when 7 then '7' when 8 then '8' when 9 then '9' when 10 then '10' when 11 then '11' end + {nchar}'";
                                case "mm": return $"' + substring(convert(char(8), {left}, 24), 4, 2) + {nchar}'";
                                case "m": return $"' + case when substring(convert(char(8), {left}, 24), 4, 1) = '0' then substring(convert(char(8), {left}, 24), 5, 1) else substring(convert(char(8), {left}, 24), 4, 2) end + {nchar}'";
                                case "ss": return $"' + substring(convert(char(8), {left}, 24), 7, 2) + {nchar}'";
                                case "s": return $"' + case when substring(convert(char(8), {left}, 24), 7, 1) = '0' then substring(convert(char(8), {left}, 24), 8, 1) else substring(convert(char(8), {left}, 24), 7, 2) end + {nchar}'";
                                case "tt": return $"' + case when cast(case when substring(convert(char(8), {left}, 24), 1, 1) = '0' then substring(convert(char(8), {left}, 24), 2, 1) else substring(convert(char(8), {left}, 24), 1, 2) end as int) >= 12 then 'PM' else 'AM' end + {nchar}'";
                                case "t": return $"' + case when cast(case when substring(convert(char(8), {left}, 24), 1, 1) = '0' then substring(convert(char(8), {left}, 24), 2, 1) else substring(convert(char(8), {left}, 24), 1, 2) end as int) >= 12 then 'P' else 'A' end + {nchar}'";
                            }
                            return m.Groups[0].Value;
                        });
                        return isMatched == false ? args1 : $"({args1})";
                }
            }
            return null;
        }
        public override string ExpressionLambdaToSqlCallTimeSpan(MethodCallExpression exp, ExpTSC tsc)
        {
            Func<Expression, string> getExp = exparg => ExpressionLambdaToSql(exparg, tsc);
            if (exp.Object == null)
            {
                switch (exp.Method.Name)
                {
                    case "Compare": return $"({getExp(exp.Arguments[0])}-({getExp(exp.Arguments[1])}))";
                    case "Equals": return $"({getExp(exp.Arguments[0])} = {getExp(exp.Arguments[1])})";
                    case "FromDays": return $"(({getExp(exp.Arguments[0])})*{60 * 60 * 24})";
                    case "FromHours": return $"(({getExp(exp.Arguments[0])})*{60 * 60})";
                    case "FromMilliseconds": return $"(({getExp(exp.Arguments[0])})/1000)";
                    case "FromMinutes": return $"(({getExp(exp.Arguments[0])})*60)";
                    case "FromSeconds": return $"({getExp(exp.Arguments[0])})";
                    case "FromTicks": return $"(({getExp(exp.Arguments[0])})/10000000)";
                    case "Parse": return $"cast({getExp(exp.Arguments[0])} as bigint)";
                    case "ParseExact":
                    case "TryParse":
                    case "TryParseExact": return $"cast({getExp(exp.Arguments[0])} as bigint)";
                }
            }
            else
            {
                var left = getExp(exp.Object);
                var args1 = exp.Arguments.Count == 0 ? null : getExp(exp.Arguments[0]);
                switch (exp.Method.Name)
                {
                    case "Add": return $"({left}+{args1})";
                    case "Subtract": return $"({left}-({args1}))";
                    case "Equals": return $"({left} = {args1})";
                    case "CompareTo": return $"({left}-({args1}))";
                    case "ToString": return $"cast({left} as varchar(100))";
                }
            }
            return null;
        }
        public override string ExpressionLambdaToSqlCallConvert(MethodCallExpression exp, ExpTSC tsc)
        {
            Func<Expression, string> getExp = exparg => ExpressionLambdaToSql(exparg, tsc);
            if (exp.Object == null)
            {
                switch (exp.Method.Name)
                {
                    case "ToBoolean": return $"(cast({getExp(exp.Arguments[0])} as varchar) not in ('0','false'))";
                    case "ToByte": return $"cast({getExp(exp.Arguments[0])} as tinyint)";
                    case "ToChar": return $"substring(cast({getExp(exp.Arguments[0])} as varchar),1,1)";
                    case "ToDateTime": return ExpressionConstDateTime(exp.Arguments[0]) ?? $"cast({getExp(exp.Arguments[0])} as datetime)";
                    case "ToDecimal": return $"cast({getExp(exp.Arguments[0])} as numeric(36,18))";
                    case "ToDouble": return $"cast({getExp(exp.Arguments[0])} as numeric(32,16))";
                    case "ToInt16": return $"cast({getExp(exp.Arguments[0])} as smallint)";
                    case "ToInt32": return $"cast({getExp(exp.Arguments[0])} as int)";
                    case "ToInt64": return $"cast({getExp(exp.Arguments[0])} as bigint)";
                    case "ToSByte": return $"cast({getExp(exp.Arguments[0])} as tinyint)";
                    case "ToSingle": return $"cast({getExp(exp.Arguments[0])} as numeric(14,7))";
                    case "ToString":
                        var gentype = exp.Arguments[0].Type.NullableTypeOrThis();
                        return gentype == typeof(Guid) ?
                            $"cast({getExp(exp.Arguments[0])} as char(36))" :
                            $"cast({getExp(exp.Arguments[0])} as {(gentype.IsNumberType() || gentype.IsEnum ? "varchar(100)" : "text")})";
                    case "ToUInt16": return $"cast({getExp(exp.Arguments[0])} as smallint)";
                    case "ToUInt32": return $"cast({getExp(exp.Arguments[0])} as int)";
                    case "ToUInt64": return $"cast({getExp(exp.Arguments[0])} as numeric(20,0))";
                }
            }
            return null;
        }
    }
}
