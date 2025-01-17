﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

#if FUCK_LUA
using ILRuntime.CLR.Utils;
using ILRuntime.Reflection;
using ILRuntime.Runtime.Intepreter;
#endif

namespace CatJson
{
    public static partial class JsonParser
    {
        /// <summary>
        /// 解析json文本为指定类型的对象实例
        /// </summary>
        public static T ParseJson<T>(string json, bool reflection = true)
        {
            return (T)ParseJson(json, typeof(T), reflection);
        }

        /// <summary>
        /// 解析json文本为指定类型的对象实例
        /// </summary>
        public static object ParseJson(string json, Type type, bool reflection = true)
        {
            Lexer.SetJsonText(json);

            object result = null;

            if (reflection)
            {
                //使用反射解析

                if (Util.IsArrayOrListType(type) || Util.IsDictionaryType(type))
                {
                    //数组或list或字典
                    TokenType nextTokenType = Lexer.LookNextTokenType();
                    result = ParseJsonValueByType(nextTokenType, type);

                }
                else
                {
                    //数据类
                    result = ParseJsonObjectByType(type);
                }
            }
            else
            {
                if (GenJsonCodes.ParseJsonCodeFuncDict.TryGetValue(type, out Func<object> func))
                {
                    //使用预生成代码解析
                    result = func();
                }
                else
                {
                    throw new Exception($"没有为{type}类型预生成的解析代码");
                }  
            }

            if (result != null && result is IJsonParserCallbackReceiver receiver)
            {
                //触发解析结束回调
                receiver.OnParseJsonEnd();
            }

            return result;
        }


        /// <summary>
        /// 解析json值为指定类型的实例值
        /// </summary>
        public static object ParseJsonValueByType(TokenType nextTokenType, Type type)
        {
            Type realType = CheckType(type);

            if (ExtensionParseFuncDict.TryGetValue(realType, out Func<object> func))
            {
                //自定义解析
                return func();
            }

            switch (nextTokenType)
            {
                case TokenType.Null:
                    Lexer.GetNextToken(out _);
                    if (!realType.IsValueType)
                    {
                        return null;
                    }
                    break;

                case TokenType.True:
                case TokenType.False:
                    Lexer.GetNextToken(out _);
                    if (realType == typeof(bool))
                    {
                        return nextTokenType == TokenType.True;
                    }
                    break;

                case TokenType.Number:
                    RangeString token = Lexer.GetNextToken(out _);
                    string str = token.ToString();
                    if (realType == typeof(byte))
                    {
                        return byte.Parse(str);
                    }
                    if (realType == typeof(int))
                    {
                        return int.Parse(str);
                    }
                    if (realType == typeof(long))
                    {
                        return long.Parse(str);
                    }
                    if (realType == typeof(float))
                    {
                        return float.Parse(str);
                    }
                    if (realType == typeof(double))
                    {
                        return double.Parse(str);
                    }
                    
                    if (realType == typeof(sbyte))
                    {
                        return sbyte.Parse(str);
                    }
                    if (realType == typeof(short))
                    {
                        return short.Parse(str);
                    }
                    if (realType == typeof(uint))
                    {
                        return uint.Parse(str);
                    }
                    if (realType == typeof(ulong))
                    {
                        return ulong.Parse(str);
                    }
                    if (realType == typeof(ushort))
                    {
                        return ushort.Parse(str);
                    }
                    if (realType == typeof(decimal))
                    {
                        return decimal.Parse(str);
                    }
                    
#if FUCK_LUA
                    if (type is ILRuntimeType ilrtType && ilrtType.ILType.IsEnum)
                    {
                        //热更层枚举 
                        return int.Parse(str);
                    }
#endif

                    if (realType.IsEnum)
                    {
                        //枚举
                        int enumInt = int.Parse(str);
                        return Enum.ToObject(realType, enumInt);
                    }
                    break;

                case TokenType.String:
                    token = Lexer.GetNextToken(out _);
                    if (realType == typeof(string))
                    {
                        return token.ToString();
                    }
                    if (realType == typeof(char))
                    {
                        return char.Parse(token.ToString());
                    }
                    break;

                case TokenType.LeftBracket:

                    if (Util.IsArrayOrListType(type))
                    {
                        Type elementType;

                        if (type.IsArray)
                        {
                            //数组
#if FUCK_LUA
                            if (type is ILRuntimeWrapperType wt)
                            {
                                elementType = wt.CLRType.ElementType.ReflectionType;
                            }
                            else
#endif
                            {
                                elementType = type.GetElementType();
                            }
                        }
                        else
                        {
                            //List
#if FUCK_LUA
                            if (type is ILRuntimeWrapperType wt)
                            {
                                elementType = wt.CLRType.GenericArguments[0].Value.ReflectionType;
                            }
                            else
#endif
                            {
                                elementType = type.GenericTypeArguments[0];
                            }

                        }

                        return ParseJsonArrayByType(type,elementType);
                    }

                    break;

                case TokenType.LeftBrace:

                    if (Util.IsDictionaryType(type))
                    {
                        //字典

                        Type valueType;
#if FUCK_LUA
                        if (type is ILRuntimeWrapperType wt2)
                        {
                            valueType = wt2.CLRType.GenericArguments[1].Value.ReflectionType;
                        }
                        else
#endif
                        {
                            valueType = type.GetGenericArguments()[1];
                        }

                        return ParseJsonObjectByDict(type, valueType);
                    }

                    //类对象
                    return ParseJsonObjectByType(type);

            }

            throw new Exception("ParseJsonValueByType调用失败，tokenType == " + nextTokenType + ",type == " + type.FullName);
        }

        /// <summary>
        /// 解析json对象为指定类型的数据类实例
        /// </summary>
        public static object ParseJsonObjectByType(Type type)
        {
            object obj = CreateInstance(type);

            if (!propertyInfoDict.ContainsKey(type) && !fieldInfoDict.ContainsKey(type))
            {
                //初始化反射信息
                AddToReflectionMap(type);
            }

            ParseJsonObjectProcedure(obj, type,false, (userdata1, userdata2,isIntKey, key, nextTokenType) => {

                Type t = (Type)userdata2;

                propertyInfoDict.TryGetValue(t, out Dictionary<RangeString, PropertyInfo> dict1);
                if (dict1 != null && dict1.TryGetValue(key, out PropertyInfo pi))
                {
                    //先尝试获取名为key的属性
                    object value = ParseJsonValueByType(nextTokenType, pi.PropertyType);
                    pi.SetValue(userdata1, value);
                }
                else
                {
                    //属性没有 再试试字段
                    fieldInfoDict.TryGetValue(t, out Dictionary<RangeString, FieldInfo> dict2);
                    if (dict2 != null && dict2.TryGetValue(key, out FieldInfo fi))
                    {
                        object value = ParseJsonValueByType(nextTokenType, fi.FieldType);
                        fi.SetValue(userdata1, value);
                    }
                    else
                    {
                        //这个json key既不是数据类的字段也不是属性，跳过
                        ParseJsonValue(nextTokenType);
                    }
                }
            });

            return obj;

        }

        /// <summary>
        /// 解析Json数组为指定类型的Array或List<T>
        /// </summary>
        private static object ParseJsonArrayByType(Type arrayType,Type elementType)
        {
            IList list;
            if (arrayType.IsArray)
            {
                //数组
                list = new List<object>();
            }
            else
            {
                //List<T>
                list = (IList)Activator.CreateInstance(arrayType);
            }

            ParseJsonArrayProcedure(list, elementType, (userdata1, userdata2, nextTokenType) =>
            {
                object value = ParseJsonValueByType(nextTokenType, (Type)userdata2);
                ((IList)userdata1).Add(value);
            });

            //返回List<T>
            if (!arrayType.IsArray)
            {
                return list;
            }

            //返回数组
            Array array = Array.CreateInstance(elementType, list.Count);
            for (int i = 0; i < list.Count; i++)
            {

                object element = list[i];
                array.SetValue(element, i);
            }

            return array;



        }

        /// <summary>
        /// 解析json对象为字典，key为string或int类型
        /// </summary>
        private static object ParseJsonObjectByDict(Type dictType, Type valueType)
        {
            IDictionary dict = (IDictionary)Activator.CreateInstance(dictType);
            Type keyType = dictType.GetGenericArguments()[0];
            ParseJsonObjectProcedure(dict, valueType,keyType == typeof(int), (userdata1, userdata2,isIntKey, key, nextTokenType) => {
                Type t = (Type)userdata2;
                object value = ParseJsonValueByType(nextTokenType, t);
                if (!isIntKey)
                {
                    ((IDictionary)userdata1).Add(key.ToString(), value);
                }
                else
                {
                    //处理字典key为int的情况
                    ((IDictionary)userdata1).Add(int.Parse(key.ToString()), value);
                }
               
            });

            return dict;
        }


     

    }
}

