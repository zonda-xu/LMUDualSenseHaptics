using LMUDualSenseHaptics.Core;
namespace LMUDualSenseHaptics.Config;

public static class UiLanguageStore
{
    public static string FilePath=>Path.Combine(ProfileStore.DirectoryPath,"ui-language.txt");
    public static string Load(string? path=null)
    {
        path??=FilePath;
        if(!File.Exists(path))return "zh-CN";
        string code=File.ReadAllText(path).Trim();
        if(!L.IsSupported(code))throw new InvalidDataException("Unsupported UI language: "+code);
        return code;
    }
    public static void Save(string code,string? path=null)
    {
        if(!L.IsSupported(code))throw new ArgumentOutOfRangeException(nameof(code));
        path=Path.GetFullPath(path??FilePath);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,code,System.Text.Encoding.ASCII);File.Move(temporary,path,true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
