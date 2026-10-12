using System.Text.Json;
using LMUDualSenseHaptics.Core;
namespace LMUDualSenseHaptics.Config;
public static class ProfileStore
{
    public static readonly JsonSerializerOptions JsonCompact=new(){PropertyNameCaseInsensitive=true};
    public static readonly JsonSerializerOptions Json=new(){WriteIndented=true,PropertyNameCaseInsensitive=true};
    public static string DirectoryPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LMUDualSenseHaptics");
    public static string FilePath=>Path.Combine(DirectoryPath,"profile.json");
    public static Profile Load(string? path=null){path??=FilePath;if(!File.Exists(path))return new();var p=JsonSerializer.Deserialize<Profile>(File.ReadAllText(path),Json)??throw new InvalidDataException("配置为空");p.Validate();return p;}
    public static VehiclePreset LoadPreset(string path)
    {
        var preset=JsonSerializer.Deserialize<VehiclePreset>(File.ReadAllText(path),Json)??throw new InvalidDataException("车型预设为空");
        preset.Validate();return preset;
    }
    public static void Save(Profile p,string? path=null)
    {
        p.Validate();path??=FilePath;Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{using(var fs=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(fs,p,Json);fs.Flush(true);}if(File.Exists(path)&&Path.GetFullPath(path)==Path.GetFullPath(FilePath))
            File.Copy(path,path+".backup-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+"-"+Guid.NewGuid().ToString("N")[..6],false);
        File.Move(tmp,path,true);}finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
}
