using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Config;
namespace LMUDualSenseHaptics;

public partial class MainWindow
{
    private sealed record TriggerChoice(string Model,string Category,string Name,bool Exists=true)
    { public bool Common => Model.Length==0; }
    private string vehicleScopeSignature="";
    private TriggerChoice EditingScope => VehicleTriggerChoice.SelectedItem as TriggerChoice ?? new("","","通用设置");
    private TriggerSettings EditingTriggers => EditingScope.Common?TriggerProfiles.Common(runtime.Profile):
        runtime.Profile.VehicleTriggers.FirstOrDefault(v=>v.Matches(EditingScope.Model,EditingScope.Category))?.Settings??TriggerProfiles.Common(runtime.Profile);
    private void SetTriggers(TriggerSettings settings)
    {
        var scope=EditingScope;
        runtime.SetProfile(scope.Common?runtime.Profile with{TriggerDefaults=settings}:
            TriggerProfiles.SaveVehicle(runtime.Profile,scope.Model,scope.Category,settings));
    }
    private void RefreshTriggerScopes(bool force=false)
    {
        var f=runtime.Current.Frame;
        bool known=VehicleTriggerSettings.HasIdentity(f);
        string signature=known?f!.VehicleModel.Trim()+"\n"+f.VehicleClass.Trim():"";
        if(!force&&signature==vehicleScopeSignature)return;
        vehicleScopeSignature=signature;
        var old=EditingScope;
        var choices=new List<TriggerChoice>{new("","","通用设置 · 未配置车辆使用")};
        choices.AddRange(runtime.Profile.VehicleTriggers.OrderBy(v=>v.VehicleModel,StringComparer.OrdinalIgnoreCase)
            .Select(v=>new TriggerChoice(v.VehicleModel,v.VehicleClass,v.VehicleModel+" · "+v.VehicleClass)));
        var current=known?choices.FirstOrDefault(c=>!c.Common&&new VehicleTriggerSettings(c.Model,c.Category,new()).Matches(f!)):null;
        if(known&&current is null)
        {
            current=new(f!.VehicleModel.Trim(),f.VehicleClass.Trim(),f.VehicleModel+" · "+f.VehicleClass+"（未配置）",false);
            choices.Add(current);
        }
        bool before=building;building=true;
        try
        {
            VehicleTriggerChoice.ItemsSource=choices;
            VehicleTriggerChoice.SelectedItem=FollowVehicleCheck.IsChecked==true?(current??choices[0]):
                choices.FirstOrDefault(c=>c.Model==old.Model&&c.Category==old.Category)??choices[0];
            UpdateTriggerScopeControls();
        }
        finally{building=before;}
        if(!before)BuildSettings();
    }
    private void UpdateTriggerScopeControls()
    {
        var scope=EditingScope;
        TriggerEditor.IsEnabled=scope.Exists;
        CreateVehicleButton.IsEnabled=!scope.Common&&!scope.Exists;
        RemoveVehicleButton.IsEnabled=!scope.Common&&scope.Exists;
        ExportVehicleButton.IsEnabled=!scope.Common&&scope.Exists;
        TriggerScopeHint.Text=scope.Common?"正在编辑通用扳机设置。已有专属配置的车辆不受影响。":scope.Exists?
            "正在编辑这辆车的 L2 / R2。更改立即生效；点击保存配置后，下次启动继续使用。":
            "此车当前使用通用设置。点击“为此车创建配置”复制通用设置，再单独调整。";
    }
    private void VehicleTriggerChoiceChanged(object sender,SelectionChangedEventArgs e)
    {
        if(building||!IsInitialized||FollowVehicleCheck is null)return;
        building=true;FollowVehicleCheck.IsChecked=false;building=false;
        UpdateTriggerScopeControls();BuildSettings();
    }
    private void FollowVehicleChanged(object sender,RoutedEventArgs e)
    {
        if(building||!IsInitialized||VehicleTriggerChoice is null)return;
        RefreshTriggerScopes(true);
    }
    private void CreateVehicleClick(object sender,RoutedEventArgs e)
    {
        if(runtime.RecordingPath is not null)return;
        try
        {
            var scope=EditingScope;if(scope.Common||scope.Exists)return;
            runtime.SetProfile(TriggerProfiles.SaveVehicle(runtime.Profile,scope.Model,scope.Category,TriggerProfiles.Common(runtime.Profile)));
            RefreshTriggerScopes(true);ChangedNotice();
        }
        catch(Exception ex){Notify(ex.Message);}
    }
    private void ExportVehicleClick(object sender,RoutedEventArgs e)
    {
        if(runtime.RecordingPath is not null)return;
        try
        {
            var scope=EditingScope;if(scope.Common||!scope.Exists)return;
            var vehicle=runtime.Profile.VehicleTriggers.First(v=>v.Matches(scope.Model,scope.Category));
            string name=string.Concat(vehicle.VehicleModel.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c));
            var dialog=new Microsoft.Win32.SaveFileDialog {Filter="车辆扳机配置 (*.json)|*.json",
                FileName=name+"-triggers.json",DefaultExt=".json",AddExtension=true,Title="分享这辆车的扳机配置"};
            if(dialog.ShowDialog(this)!=true)return;
            VehicleTriggerShare.Export(vehicle,dialog.FileName);
            Notify("已导出 "+vehicle.VehicleModel+" 的扳机配置，将此 JSON 文件发送给其他用户即可分享。");
        }
        catch(Exception ex){Notify("导出失败："+ex.Message);}
    }
    private void ImportVehicleClick(object sender,RoutedEventArgs e)
    {
        if(runtime.RecordingPath is not null)return;
        try
        {
            var dialog=new Microsoft.Win32.OpenFileDialog {Filter="车辆扳机配置 (*.json)|*.json",Title="导入车辆扳机配置"};
            if(dialog.ShowDialog(this)!=true)return;
            var vehicle=VehicleTriggerShare.Import(dialog.FileName);
            if(runtime.Profile.VehicleTriggers.Any(v=>v.Matches(vehicle.VehicleModel,vehicle.VehicleClass)) &&
                MessageBox.Show(this,"替换 "+vehicle.VehicleModel+" · "+vehicle.VehicleClass+" 已有的扳机配置？",
                    "导入车辆配置",MessageBoxButton.OKCancel,MessageBoxImage.Question)!=MessageBoxResult.OK)return;
            var updated=VehicleTriggerShare.Apply(runtime.Profile,vehicle);
            ProfileStore.Save(updated);runtime.SetProfile(updated);
            building=true;FollowVehicleCheck.IsChecked=false;building=false;
            RefreshTriggerScopes(true);
            building=true;
            VehicleTriggerChoice.SelectedItem=VehicleTriggerChoice.Items.Cast<TriggerChoice>().First(c=>
                !c.Common&&vehicle.Matches(c.Model,c.Category));
            building=false;UpdateTriggerScopeControls();BuildSettings();
            Notify("已导入并保存 "+vehicle.VehicleModel+" · "+vehicle.VehicleClass+"；识别到该车型时自动启用。");
        }
        catch(Exception ex){building=false;Notify("导入失败："+ex.Message);}
    }
    private void RemoveVehicleClick(object sender,RoutedEventArgs e)
    {
        if(runtime.RecordingPath is not null)return;
        var scope=EditingScope;if(scope.Common||!scope.Exists)return;
        if(MessageBox.Show(this,"删除 "+scope.Name+" 的专属扳机设置？该车将恢复使用通用设置。","删除车辆配置",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
        try
        {
            runtime.SetProfile(runtime.Profile with{VehicleTriggers=runtime.Profile.VehicleTriggers.Where(v=>!v.Matches(scope.Model,scope.Category)).ToImmutableArray()});
            RefreshTriggerScopes(true);ChangedNotice();
        }
        catch(Exception ex){Notify(ex.Message);}
    }
}
