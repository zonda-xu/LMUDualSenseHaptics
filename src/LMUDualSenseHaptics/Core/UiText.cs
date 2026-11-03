using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace LMUDualSenseHaptics.Core;

// Retain the message and arguments until presentation, so a cached device error
// or diagnostic changes language immediately without restarting a hardware loop.
[JsonConverter(typeof(UiTextJsonConverter))]
public sealed record UiText(string? Key,object?[]? Arguments=null,bool Literal=false):IFormattable
{
    public bool IsEmpty=>Key is null&&Arguments is null || Literal&&string.IsNullOrEmpty(Key);
    public string Render(string language)
    {
        if(Literal)return Key??"";
        object?[] values=Arguments?.Select(a=>a is UiText text?(object)text.Render(language):a).ToArray()??[];
        if(Key is null)return string.Concat(values);
        return string.Format(L.Culture(language),L.Get(Key,language),values);
    }
    public override string ToString()=>Render(L.Language);
    public string ToString(string? format,IFormatProvider? provider)=>ToString();
    public static implicit operator UiText(string text)=>new(text,null,true);
    public static UiText operator +(UiText a,UiText b)=>new(null,[a,b]);
    public static UiText operator +(UiText a,string b)=>a+(UiText)b;
    public static UiText operator +(UiText a,object b)=>new(null,[a,b]);
    public static UiText operator +(string a,UiText b)=>(UiText)a+b;
    public static UiText Join(string separator,IEnumerable<UiText> items)
    {var values=items.ToArray();return new(null,values.SelectMany((v,i)=>i==0?new object?[]{v}:new object?[]{separator,v}).ToArray());}
}
public sealed class UiTextJsonConverter:JsonConverter<UiText>
{
    public override UiText Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)=>reader.GetString()??"";
    // Logs keep a stable language and schema; UI language never changes replay data.
    public override void Write(Utf8JsonWriter writer,UiText value,JsonSerializerOptions options)=>writer.WriteStringValue(value.Render("zh-CN"));
}
