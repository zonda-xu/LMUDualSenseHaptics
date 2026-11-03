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
    private TriggerChoice EditingScope => VehicleTriggerChoice.SelectedItem as TriggerChoice ?? new("","",L.T("Text_18A9D61DEE"));
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
        var choices=new List<TriggerChoice>{new("","",L.T("Text_7B7E421536"))};
        choices.AddRange(runtime.Profile.VehicleTriggers.OrderBy(v=>v.VehicleModel,StringComparer.OrdinalIgnoreCase)
            .Select(v=>new TriggerChoice(v.VehicleModel,v.VehicleClass,v.VehicleModel+" · "+v.VehicleClass)));
        var current=known?choices.FirstOrDefault(c=>!c.Common&&new VehicleTriggerSettings(c.Model,c.Category,new()).Matches(f!)):null;
        if(known&&current is null)
        {
            current=new(f!.VehicleModel.Trim(),f.VehicleClass.Trim(),L.F("Text_C93597E7CD", f.VehicleModel, f.VehicleClass),false);
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
        TriggerScopeHint.Text=scope.Common?L.T("Text_E9DFBBC15A"):scope.Exists?
            L.T("Text_92352A3975"):
            L.T("Text_12EBD71A3E");
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
        catch(Exception ex){Notify(L.Error(ex).ToString());}
    }
    private void ExportVehicleClick(object sender,RoutedEventArgs e)
    {
        if(runtime.RecordingPath is not null)return;
        try
        {
            var scope=EditingScope;if(scope.Common||!scope.Exists)return;
            var vehicle=runtime.Profile.VehicleTriggers.First(v=>v.Matches(scope.Model,scope.Category));
            string name=string.Concat(vehicle.VehicleModel.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c));
            var dialog=new Microsoft.Win32.SaveFileDialog {Filter=L.T("Text_FAE5E2E597"),
                FileName=name+"-triggers.json",DefaultExt=".json",AddExtension=true,Title=L.T("Text_C392551595")};
            if(dialog.ShowDialog(this)!=true)return;
            VehicleTriggerShare.Export(vehicle,dialog.FileName);
            Notify(L.F("Text_AB32F7591C", vehicle.VehicleModel));
        }
        catch(Exception ex){Notify(L.T("Text_C55E9D64E5")+L.Error(ex).ToString());}
    }
    private void ImportVehicleClick(object sender,RoutedEventArgs e)
    {
        if(runtime.RecordingPath is not null)return;
        try
        {
            var dialog=new Microsoft.Win32.OpenFileDialog {Filter=L.T("Text_FAE5E2E597"),Title=L.T("Text_96618B11E9")};
            if(dialog.ShowDialog(this)!=true)return;
            var vehicle=VehicleTriggerShare.Import(dialog.FileName);
            if(runtime.Profile.VehicleTriggers.Any(v=>v.Matches(vehicle.VehicleModel,vehicle.VehicleClass)) &&
                !Confirm(L.F("Text_492C68AEF0", vehicle.VehicleModel, vehicle.VehicleClass), L.T("Text_340152EE2E")))return;
            var updated=VehicleTriggerShare.Apply(runtime.Profile,vehicle);
            ProfileStore.Save(updated);runtime.SetProfile(updated);
            building=true;FollowVehicleCheck.IsChecked=false;building=false;
            RefreshTriggerScopes(true);
            building=true;
            VehicleTriggerChoice.SelectedItem=VehicleTriggerChoice.Items.Cast<TriggerChoice>().First(c=>
                !c.Common&&vehicle.Matches(c.Model,c.Category));
            building=false;UpdateTriggerScopeControls();BuildSettings();
            Notify(L.F("Text_1E7379269F", vehicle.VehicleModel, vehicle.VehicleClass));
        }
        catch(Exception ex){building=false;Notify(L.T("Text_F806C8CB6B")+L.Error(ex).ToString());}
    }
    private void RemoveVehicleClick(object sender,RoutedEventArgs e)
    {
        if(runtime.RecordingPath is not null)return;
        var scope=EditingScope;if(scope.Common||!scope.Exists)return;
        if(!Confirm(L.F("Text_334808FCA8", scope.Name),L.T("Text_8870137E73")))return;
        try
        {
            runtime.SetProfile(runtime.Profile with{VehicleTriggers=runtime.Profile.VehicleTriggers.Where(v=>!v.Matches(scope.Model,scope.Category)).ToImmutableArray()});
            RefreshTriggerScopes(true);ChangedNotice();
        }
        catch(Exception ex){Notify(L.Error(ex).ToString());}
    }
}
