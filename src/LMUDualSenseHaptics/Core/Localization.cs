using System.Globalization;
using System.Reflection;
using System.Text.Json;
namespace LMUDualSenseHaptics.Core;

public static class L
{
    public sealed record LanguageOption(string Code,string Name);
    public static IReadOnlyList<LanguageOption> Languages {get;}=Array.AsReadOnly(new[]{
        new LanguageOption("zh-CN","中文"),new("en","English"),new("fr","Français"),
        new("es","Español"),new("ru","Русский"),new("ar","العربية")});
    private static readonly Dictionary<string,Dictionary<string,string>> Catalogs=Languages.ToDictionary(x=>x.Code,x=>Load(x.Code));
    private static string language="zh-CN";
    public static string Language=>Volatile.Read(ref language);
    public static bool IsSupported(string code)=>Languages.Any(x=>x.Code==code);
    public static void SetLanguage(string code)
    {if(!IsSupported(code))throw new ArgumentOutOfRangeException(nameof(code));Volatile.Write(ref language,code);}
    public static CultureInfo Culture(string code)=>CultureInfo.GetCultureInfo(code);
    private static Dictionary<string,string> Load(string code)
    {
        using var stream=typeof(L).Assembly.GetManifestResourceStream("LMUDualSenseHaptics.Localization."+code+".json")
            ??throw new InvalidDataException("Missing language resource: "+code);
        return JsonSerializer.Deserialize<Dictionary<string,string>>(stream)??throw new InvalidDataException("Invalid language resource: "+code);
    }
    public static IReadOnlyDictionary<string,string> Resources(string code)=>Catalogs[code];
    public static string Get(string key,string code)=>Catalogs[code][key];
    public static string T(string key)=>Get(key,Language);
    public static string F(string key,params object?[] args)=>M(key,args).ToString();
    public static UiText M(string key,params object?[] args)=>new(key,args);
    public static UiText Error(Exception exception)=>exception.Data["UiText"] is UiText text?text:(UiText)exception.Message;
    private static T Attach<T>(T error,UiText text) where T:Exception {error.Data["UiText"]=text;return error;}
    public static InvalidDataException InvalidData(UiText text)=>Attach(new InvalidDataException(text.ToString()),text);
    public static InvalidOperationException InvalidOperation(UiText text)=>Attach(new InvalidOperationException(text.ToString()),text);
    public static ArgumentException Argument(UiText text)=>Attach(new ArgumentException(text.ToString()),text);
    public static IOException IO(UiText text)=>Attach(new IOException(text.ToString()),text);
    public static TimeoutException Timeout(UiText text)=>Attach(new TimeoutException(text.ToString()),text);
}
