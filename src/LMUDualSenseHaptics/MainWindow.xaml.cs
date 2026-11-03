using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Hardware;
using LMUDualSenseHaptics.Config;
using LMUDualSenseHaptics.Runtime;
using LMUDualSenseHaptics.Theme;
namespace LMUDualSenseHaptics;

public partial class MainWindow:Window
{
    private readonly Dictionary<string,TextBlock> channelLabels=new();
    private readonly Dictionary<string,FrameworkElement> featureRows=new();
    private readonly ApplicationCoordinator runtime=new();
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private sealed record AudioChoice(string Id,string Name);
    private string audioEndpointSignature="";
    private bool closing,closed,building;
    public MainWindow()
    {
        L.SetLanguage(UiLanguageStore.Load());
        InitializeComponent();InitializeLanguage();App.RegisterEmergencyStop(runtime.EmergencyStop);
        try{runtime.SetProfile(ProfileStore.Load());}
        catch(Exception ex){Notify(L.T("Text_AC6A98F7A2")+L.Error(ex).ToString());}
        ThemeManager.Apply(runtime.Profile.UiTheme);
        ThemeButton.Content=runtime.Profile.UiTheme=="Dark"?L.T("Text_02470E632F"):L.T("Text_46BAD67D06");
        RefreshTriggerScopes(true);timer.Tick+=(_,_)=>Refresh();timer.Start();Closing+=OnClosing;runtime.Start();
    }
    private void Notify(string text)=>NoticeText.Text=text;
    private void ChangedNotice()=>Notify(L.T("Text_46B24E8C40"));
    private void BuildSettings()
    {
        building=true;
        try
        {
            foreach(var panel in new[]{BrakeSettings,ThrottleSettings,ExtraTriggerSettings,RoadSettings,OtherGripSettings,SpatialSettings,AudioSettings})panel.Children.Clear();
            channelLabels.Clear();featureRows.Clear();
            AddFeature(ThrottleSettings,"Tc",L.T("Text_717E16F6E5"),L.T("Text_7DC923F491"));
            AddFeature(ExtraTriggerSettings,"ThrottleBase",L.T("Text_20F1B8CACB"),L.T("Text_2E87F971FE"));
            AddFeature(ExtraTriggerSettings,"Wheelspin",L.T("Text_0841CA3BC7"),L.T("Text_874FF664FE"));
            AddFeature(ExtraTriggerSettings,"Redline",L.T("Text_248F8E41F4"),L.T("Text_D80210284A"));
            AddFeature(ExtraTriggerSettings,"Shift",L.T("Text_0BE0426112"),L.T("Text_C52D5B0FC1"));
            AddFeature(BrakeSettings,"BrakeBase",L.T("Text_3E0454700A"),L.T("Text_49A067BFE7"));
            BrakeModeChoice.SelectedIndex=(int)EditingTriggers.BrakeMode;
            AddFeature(BrakeSettings,"Abs",L.T("Text_7D43DF8DB0"),L.T("Text_CEA484531F"));
            AddFeature(BrakeSettings,"Lock",L.T("Text_439CC3C8A9"),L.T("Text_39220DC61D"));
            var brakeResponse=EditingTriggers.BrakeResponse;
            AddSetting(L.T("Text_69DE6CAEC3"),0,30,(int)(brakeResponse.TravelDeadzone*100),L.T("Text_2442F7AF07"),v=>SetBrakeResponse(EditingTriggers.BrakeResponse with{TravelDeadzone=v/100.0}),"%",BrakeSettings);
            AddSetting(L.T("Text_7CB361A9BE"),50,100,(int)(brakeResponse.TravelEnd*100),L.T("Text_030A50EADF"),v=>SetBrakeResponse(EditingTriggers.BrakeResponse with{TravelEnd=v/100.0}),"%",BrakeSettings);
            AddSetting(L.T("Text_1146995631"),25,400,(int)(brakeResponse.InputExponent*100),L.T("Text_B937AF696D"),v=>SetBrakeResponse(EditingTriggers.BrakeResponse with{InputExponent=v/100.0})," /100",BrakeSettings);
            AddFeature(RoadSettings,"Road",L.T("Text_6B2E003D89"),L.T("Text_2B21636A7E"));
            AddFeature(RoadSettings,"Kerb",L.T("Text_E7E0BE2AA4"),L.T("Text_188772D669"));
            AddFeature(OtherGripSettings,"GripSlip",L.T("Text_367D3E0C3C"),L.T("Text_515DB81C0B"));
            AddFeature(OtherGripSettings,"Impact",L.T("Text_E21744C570"),L.T("Text_5F4E258E13"));
            AddFeature(OtherGripSettings,"Shift",L.T("Text_836EC950E4"),L.T("Text_2BA353071F"));
            AddFeature(OtherGripSettings,"Engine",L.T("Text_78A7F252A6"),L.T("Text_9E01A8D301"));
            AddSetting(L.T("Text_57003C1C74"),25,200,runtime.Profile.RoadSensitivity,L.T("Text_40DDC0FC6F"),
                v=>runtime.SetProfile(runtime.Profile with{RoadSensitivity=v}));
            AddSetting(L.T("Text_458B90C7C2"),0,100,runtime.Profile.SpatialSeparation,L.T("Text_3F9612157F"),
                v=>runtime.SetProfile(runtime.Profile with{SpatialSeparation=v}));
            var audio=runtime.Profile.AudioHaptics;
            AudioModeCheck.IsChecked=audio.Enabled;SwapAudioSidesCheck.IsChecked=audio.SwapSides;
            KerbFallbackCheck.IsChecked=audio.DynamicKerbFallback;
            AddSetting(L.T("Text_3A8405BFED"),0,100,audio.MasterStrength,L.T("Text_F77CC9FAAC"),
                v=>SetAudio(runtime.Profile.AudioHaptics with{MasterStrength=v}),"%",AudioSettings);
            AddSetting(L.T("Text_1184995BE6"),25,180,audio.RoadFrequencyHz,L.T("Text_F989A56847"),
                v=>SetAudio(runtime.Profile.AudioHaptics with{RoadFrequencyHz=v})," Hz",AudioSettings);
            AddSetting(L.T("Text_F1D0D86F24"),40,220,audio.KerbFrequencyHz,L.T("Text_23150A3860"),
                v=>SetAudio(runtime.Profile.AudioHaptics with{KerbFrequencyHz=v})," Hz",AudioSettings);
            AddSetting(L.T("Text_B757A91BC4"),20,100,audio.BumpFrequencyHz,L.T("Text_E97A9907D1"),
                v=>SetAudio(runtime.Profile.AudioHaptics with{BumpFrequencyHz=v})," Hz",AudioSettings);
            audioEndpointSignature="";RefreshAudioChoices();
            TcOnlyCheck.IsChecked=EditingTriggers.ThrottleMode==ThrottleFeedbackMode.TcOnly;
            UpdateModeControls();
        }
        finally{building=false;}
    }
    private void SetBrakeResponse(BrakeResponse response)=>SetTriggers(EditingTriggers with{BrakeResponse=response});
    private void BrakeModeChanged(object sender,SelectionChangedEventArgs e)
    {
        if(building||!IsInitialized||BrakeModeChoice is null||runtime.RecordingPath is not null)return;
        if(BrakeModeChoice.SelectedIndex is <0 or >2)return;
        try{SetTriggers(EditingTriggers with{BrakeMode=(BrakeFeedbackMode)BrakeModeChoice.SelectedIndex});ChangedNotice();}
        catch(Exception ex){Notify(L.Error(ex).ToString());}
    }
    private void AddFeature(StackPanel panel,string key,string name,string hint)
    {
        bool trigger=panel==BrakeSettings||panel==ThrottleSettings||panel==ExtraTriggerSettings;
        PropertyInfo property=(trigger?typeof(TriggerSettings):typeof(Profile)).GetProperty(key)!;
        var feature=(Feature)property.GetValue(trigger?EditingTriggers:runtime.Profile)!;
        var row=new Grid{Margin=new Thickness(0,12,0,12)};
        row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        row.ColumnDefinitions.Add(new(){Width=new GridLength(200)});
        row.ColumnDefinitions.Add(new(){Width=new GridLength(42)});
        row.ColumnDefinitions.Add(new(){Width=new GridLength(62)});
        row.RowDefinitions.Add(new(){Height=GridLength.Auto});
        row.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var title=new TextBlock{Text=name,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,16,4)};
        var description=new TextBlock{Text=hint,Style=(Style)FindResource("Muted"),Margin=new Thickness(0,0,24,0)};
        Grid.SetRow(description,1);
        var slider=new Slider{Minimum=0,Maximum=100,Value=feature.Strength,TickFrequency=1,IsSnapToTickEnabled=true,Margin=new Thickness(0,0,12,0),ToolTip=hint};
        AutomationProperties.SetName(slider,L.F("Text_54FC56B588", name));
        var value=new TextBlock{Text=feature.Strength.ToString(),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,14,0)};
        var check=new CheckBox{IsChecked=feature.Enabled,ToolTip=L.F("Text_F64012A4F2", name),VerticalAlignment=VerticalAlignment.Center};
        AutomationProperties.SetName(check,L.F("Text_F64012A4F2", name));
        var diagnostic=new TextBlock{Text=L.T("Text_8475E45FAD"),Style=(Style)FindResource("Muted"),HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,0,14,0)};
        Grid.SetColumn(slider,1);Grid.SetColumn(value,2);Grid.SetColumn(check,3);Grid.SetRowSpan(check,2);
        Grid.SetRow(diagnostic,1);Grid.SetColumn(diagnostic,1);Grid.SetColumnSpan(diagnostic,2);
        foreach(var element in new FrameworkElement[]{title,description,slider,value,check,diagnostic})row.Children.Add(element);
        panel.Children.Add(row);channelLabels[(trigger?"T:":"")+key]=diagnostic;featureRows[(trigger?"T:":"")+key]=row;
        void Changed()
        {
            if(building||runtime.RecordingPath is not null)return;
            try
            {
                var featureValue=new Feature(check.IsChecked==true,(int)slider.Value);
                if(trigger){var settings=EditingTriggers with{};property.SetValue(settings,featureValue);SetTriggers(settings);}
                else
                {
                    var profile=runtime.Profile with{TriggerDefaults=TriggerProfiles.Common(runtime.Profile)};
                    property.SetValue(profile,featureValue);runtime.SetProfile(profile);
                }
                value.Text=((int)slider.Value).ToString();ChangedNotice();
            }
            catch(Exception ex){Notify(L.Error(ex).ToString());}
        }
        slider.ValueChanged+=(_,_)=>Changed();check.Checked+=(_,_)=>Changed();check.Unchecked+=(_,_)=>Changed();
    }
    private void AddSetting(string name,int min,int max,int initial,string hint,Action<int> changed,string unit="%",StackPanel? target=null)
    {
        var row=new Grid{Margin=new Thickness(0,8,0,8)};
        row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        row.ColumnDefinitions.Add(new(){Width=new GridLength(245)});
        row.ColumnDefinitions.Add(new(){Width=new GridLength(59)});
        var label=new TextBlock{Text=name,VerticalAlignment=VerticalAlignment.Center,ToolTip=hint};
        var slider=new Slider{Minimum=min,Maximum=max,Value=initial,TickFrequency=1,IsSnapToTickEnabled=true,ToolTip=hint};
        AutomationProperties.SetName(slider,name);
        var value=new TextBlock{Text=initial+unit,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right};
        Grid.SetColumn(slider,1);Grid.SetColumn(value,2);row.Children.Add(label);row.Children.Add(slider);row.Children.Add(value);
        (target??SpatialSettings).Children.Add(row);
        slider.ValueChanged+=(_,_)=>
        {
            if(building||runtime.RecordingPath is not null)return;
            try{changed((int)slider.Value);value.Text=(int)slider.Value+unit;ChangedNotice();}
            catch(Exception ex){Notify(L.Error(ex).ToString());}
        };
    }
    private void SetAudio(AudioHapticsSettings settings)=>runtime.SetProfile(runtime.Profile with{AudioHaptics=settings});
    private void AudioOptionsChanged(object sender,RoutedEventArgs e)
    {
        if(building||runtime.RecordingPath is not null||!IsInitialized||AudioModeCheck is null||SwapAudioSidesCheck is null||KerbFallbackCheck is null)return;
        try
        {
            SetAudio(runtime.Profile.AudioHaptics with{Enabled=AudioModeCheck.IsChecked==true,
                SwapSides=SwapAudioSidesCheck.IsChecked==true,DynamicKerbFallback=KerbFallbackCheck.IsChecked==true});
            ChangedNotice();
        }
        catch(Exception ex){Notify(L.Error(ex).ToString());}
    }
    private void AudioDeviceChanged(object sender,SelectionChangedEventArgs e)
    {
        if(building||!IsInitialized||runtime.RecordingPath is not null||AudioDevicesCombo.SelectedItem is not AudioChoice selected)return;
        try{SetAudio(runtime.Profile.AudioHaptics with{EndpointId=selected.Id});ChangedNotice();}
        catch(Exception ex){Notify(L.Error(ex).ToString());}
    }
    private void RefreshAudioDevicesClick(object sender,RoutedEventArgs e)=>runtime.RefreshAudioDevices();
    private void SoundSettingsClick(object sender,RoutedEventArgs e)
    {try{Process.Start(new ProcessStartInfo("control.exe","mmsys.cpl,,0"){UseShellExecute=true});}catch(Exception ex){Notify(L.Error(ex).ToString());}}
    private void RefreshAudioChoices()
    {
        var devices=runtime.AudioEndpoints;string selected=runtime.Profile.AudioHaptics.EndpointId;
        string signature=selected+"|"+string.Join("|",devices.Select(d=>d.Id+":"+d.DisplayName));
        if(signature==audioEndpointSignature)return;
        audioEndpointSignature=signature;bool before=building;building=true;
        try
        {
            var choices=new List<AudioChoice>{new("",L.T("Text_D798B9D15B"))};
            choices.AddRange(devices.Select(d=>new AudioChoice(d.Id,d.DisplayName)));
            if(selected.Length>0&&!choices.Any(c=>c.Id==selected))choices.Add(new(selected,L.T("Text_FF99D2F025")));
            AudioDevicesCombo.ItemsSource=choices;AudioDevicesCombo.SelectedValue=selected;
        }
        finally{building=before;}
    }
    private void UpdateModeControls()
    {
        bool only=EditingTriggers.ThrottleMode==ThrottleFeedbackMode.TcOnly;
        foreach(var key in new[]{"ThrottleBase","Wheelspin","Redline","Shift"})
            if(featureRows.TryGetValue("T:"+key,out var row)){row.IsEnabled=!only;row.Opacity=only?.52:1;}
        LegacyHint.Text=only?L.T("Text_0739920D84"):
            L.T("Text_A74745D056");
    }
    private void ThrottleModeChanged(object sender,RoutedEventArgs e)
    {
        if(building||!IsInitialized||runtime.RecordingPath is not null)return;
        try
        {
            SetTriggers(EditingTriggers with{ThrottleMode=TcOnlyCheck.IsChecked==true?ThrottleFeedbackMode.TcOnly:ThrottleFeedbackMode.Legacy});
            UpdateModeControls();ChangedNotice();
        }
        catch(Exception ex){Notify(L.Error(ex).ToString());}
    }
    private void ThemeClick(object sender,RoutedEventArgs e)
    {
        if(runtime.RecordingPath is not null){Notify(L.T("Text_2E186D458A"));return;}
        var theme=runtime.Profile.UiTheme=="Dark"?"Light":"Dark";
        runtime.SetProfile(runtime.Profile with{UiTheme=theme});ThemeManager.Apply(theme);
        ThemeButton.Content=theme=="Dark"?L.T("Text_02470E632F"):L.T("Text_46BAD67D06");ChangedNotice();
    }
    private static int EncodedPeak(TriggerEffect e)
    {
        if(e.Mode==5)return 0;
        uint bits=(uint)(e.P2|(e.P3<<8)|(e.P4<<16)|(e.P5<<24));int mask=e.P0|(e.P1<<8);
        return Enumerable.Range(0,10).Max(i=>(mask&(1<<i))!=0?(int)((bits>>(i*3))&7)+1:0);
    }
    private void Refresh()
    {
        RefreshTriggerScopes();
        var active=TriggerProfiles.Find(runtime.Profile,runtime.Current.Frame);
        ActiveTriggerProfileText.Text=L.T("Text_36EF5E06D9")+(active is null?L.T("Text_18A9D61DEE"):active.VehicleModel+" · "+active.VehicleClass);
        bool recording=runtime.RecordingPath is not null;
        TriggerSettingsRoot.IsEnabled=VibrationSettingsRoot.IsEnabled=!recording;
        ThemeButton.IsEnabled=!recording;RecordButton.Content=recording?L.T("Text_B7CED79990"):L.T("Text_F04853B20F");
        var s=runtime.Current;var f=s.Frame;RefreshAudioChoices();
        var audio=s.Audio;bool audioMode=runtime.Profile.AudioHaptics.Enabled;
        AudioStatusText.Text=!audioMode?L.T("Text_257A9A6187"):audio is null?L.T("Text_AB8B184DA5"):
            audio.Message.ToString()+(audio.Ready?L.T("Text_C0EC54C490"):L.T("Text_4E6CD5A10A"));
        StateText.Text=s.State.StartsWith("Driving",StringComparison.Ordinal)?L.T("Text_4111B35AC8"):
            s.State.StartsWith("DeviceReady",StringComparison.Ordinal)?L.T("Text_8806BBDB31"):
            s.State.StartsWith("Stale",StringComparison.Ordinal)?L.T("Text_B9FEF476FB"):
            s.State is "Paused"?L.T("Text_8B4A2DC9BC"):s.State is "NotDriving"?L.T("Text_47A30715F8"):
            s.State is "WaitingForLMU"?L.T("Text_84CD590732"):s.State is "Disconnected"?L.T("Text_A57E81278D"):L.T("Text_F56BC3AACE").TrimEnd('：',':',' ');
        DeviceChip.Text=s.DeviceConnected?L.T("Text_72C4E3AC4E"):L.T("Text_72DB8F979F");
        CarText.Text=f is null?L.T("Text_792FC92035"):$"{f.VehicleModel}  /  {f.VehicleClass}  ·  {f.SpeedMps*3.6:F0} km/h  ·  {f.Rpm:F0} RPM";
        ConnectionText.Text=L.F("Text_05C9B0983F", s.TelemetryHz, s.OutputHz)+(audioMode?(audio?.Ready==true?L.T("Text_099486EC27"):L.T("Text_D6ED44A7CD")):L.T("Text_5F29C1B9C3"));
        ErrorText.Text=s.Error.ToString();ErrorBanner.Visibility=s.Error.IsEmpty?Visibility.Collapsed:Visibility.Visible;
        ValidationText.Text=L.F("Text_51318070A7", s.Telemetry, s.Device, s.ParameterSource, s.LearningState);
        int brake=EncodedPeak(s.Output.LeftTrigger),throttle=EncodedPeak(s.Output.RightTrigger);
        BrakeOutputText.Text=brake==0?L.T("Text_CC3D94550A"):(s.LeftTriggerSource switch{"Abs"=>L.T("Text_EE10564D7F"),"AbsWall"=>L.T("Text_21C48842AD"),"BrakeReference"=>L.T("Text_622833CF92"),"BrakeLimit"=>L.T("Text_06E88B07D7"),"LockPulse"=>L.T("Text_426B82E3A3"),"Lock"=>L.T("Text_D4C258D6B9"),_=>L.T("Text_8A81F759D3")})+$" · {brake} / 8";
        ThrottleOutputText.Text=throttle==0?L.T("Text_39A65126C1"):s.RightTriggerSource=="Tc"?$"TC · {throttle} / 8":$"{throttle} / 8";
        BrakeMeter.Value=brake;ThrottleMeter.Value=throttle;
        LeftGripMeter.Maximum=RightGripMeter.Maximum=audioMode?1:255;
        if(audioMode)
        {
            var levels=audio?.Levels??default;
            LeftGripText.Text=audio?.Ready==true?$"{levels.LeftRms*100:F1}%":L.T("Text_D6ED44A7CD");
            RightGripText.Text=audio?.Ready==true?$"{levels.RightRms*100:F1}%":L.T("Text_D6ED44A7CD");
            LeftGripMeter.Value=levels.LeftRms;RightGripMeter.Value=levels.RightRms;
        }
        else
        {
            LeftGripText.Text=$"{s.Output.LeftActuator} / 255";RightGripText.Text=$"{s.Output.RightActuator} / 255";
            LeftGripMeter.Value=s.Output.LeftActuator;RightGripMeter.Value=s.Output.RightActuator;
        }
        foreach(var pair in channelLabels)
        {
            bool trigger=pair.Key.StartsWith("T:",StringComparison.Ordinal);string key=trigger?pair.Key[2..]:pair.Key;
            var outputProfile=trigger?TriggerProfiles.Resolve(runtime.Profile,f):runtime.Profile;
            var feature=(Feature)typeof(Profile).GetProperty(key)!.GetValue(outputProfile)!;
            double level=(double)typeof(ChannelLevels).GetProperty(key)!.GetValue(s.Channels)!;
            bool bypass=trigger&&outputProfile.ThrottleMode==ThrottleFeedbackMode.TcOnly&&(key is "ThrottleBase" or "Wheelspin" or "Redline" or "Shift");
            pair.Value.Text=bypass?L.T("Text_83D65B3DA3"):feature.Gain==0?L.T("Text_6744B4C6A9"):!s.AdvancedFeedbackEnabled?L.T("Text_8475E45FAD"):
                level>.001?L.F("Text_C20B538FE6", level*100):L.T("Text_BCFC25B291");
        }
        EventsText.Text=$"L2 [{s.LeftTriggerSource}] 0x{s.Output.LeftTrigger.Mode:X2}  /  R2 [{s.RightTriggerSource}] 0x{s.Output.RightTrigger.Mode:X2}\n"+
            L.F("Text_DBD0BCFB43", s.Abs, s.Lock, (f?.TcIntervening==true?1:0), s.Tc, s.Spin)+
            L.F("Text_FBAF7B99F3", s.Layers.RoadL, s.Layers.RoadR, s.Layers.KerbL, s.Layers.KerbR)+
            L.F("Text_E6F0B0A703", s.Layers.SlipL, s.Layers.SlipR, Math.Max(s.Layers.ImpactL,s.Layers.ImpactR));
        WheelsText.Text=s.WheelDiagnostics.ToString()+(f is null?"":"\n"+string.Join("\n",f.Wheels.Select((w,i)=>
            $"{new[]{"FL","FR","RL","RR"}[i]} surface={w.SurfaceType?.ToString()??L.T("Text_4D8C1C5B42")} terrain={w.TerrainName} kerb={w.IsKerb} susp={w.SuspDeflectionM:F4}m tireCompression={w.TireVerticalDeflectionM*1000:F2}mm load={w.TireLoadN:F0}N")));
        PerformanceText.Text=L.F("Text_A62E19D69B", s.IntervalP50, s.IntervalP95, s.IntervalP99)+
            L.F("Text_7487E02B87", s.LatencyP50, s.LatencyP95)+
            L.F("Text_55C2FCD0CE", s.CpuPercent, s.AllocatedMBPerSecond, s.WorkingSet/1048576.0)+
            L.F("Text_EAEDBCA1EE", audio?.EndpointId??L.T("Text_3D52574CE1"), audio?.Format??L.T("Text_3D52574CE1"), audio?.BufferMilliseconds??0, audio?.SubmittedFrames??0, audio?.Levels.LeftPeak??0, audio?.Levels.RightPeak??0)+
            L.F("Text_E419ABA548", s.Dropped, runtime.RecordingPath??L.T("Text_B5FFAC63F7"));
        DeviceTestsPanel.IsEnabled=f is not {Driving:true};
    }
    private void DeviceTestClick(object sender,RoutedEventArgs e)
    {try{runtime.RequestDeviceTest((string)((Button)sender).Tag);Notify(L.T("Text_0CB4031CEE"));}catch(Exception ex){Notify(L.Error(ex).ToString());}}
    private void SaveClick(object sender,RoutedEventArgs e)
    {try{ProfileStore.Save(runtime.Profile);Notify(L.T("Text_3516103F18"));}catch(Exception ex){Notify(L.T("Text_6ADE7BAF87")+L.Error(ex).ToString());}}
    private void ResetClick(object sender,RoutedEventArgs e)
    {
        if(runtime.RecordingPath is not null){Notify(L.T("Text_7F8FE36E97"));return;}
        if(!Confirm(L.T("Text_83F291A836"),L.T("Text_F242C94797")))return;
        runtime.SetProfile(new(){VehiclePresets=runtime.Profile.VehiclePresets,RightGain=runtime.Profile.RightGain,UiTheme=runtime.Profile.UiTheme});
        RefreshTriggerScopes(true);ChangedNotice();
    }
    private async void RecordClick(object sender,RoutedEventArgs e)
    {
        try{if(runtime.RecordingPath is null){runtime.StartRecording();Notify(L.T("Text_DD5F98657D"));}else{await runtime.StopRecording();Notify(L.T("Text_E6E4261954"));}}
        catch(Exception ex){Notify(L.T("Text_137F1AF36C")+L.Error(ex).ToString());}
    }
    private void ImportPresetClick(object sender,RoutedEventArgs e)
    {
        try
        {
            if(runtime.RecordingPath is not null)throw L.InvalidOperation(L.M("Text_B6C1C4CF80"));
            var dialog=new Microsoft.Win32.OpenFileDialog{Filter=L.T("Text_B9A75A6A70")};if(dialog.ShowDialog()!=true)return;
            var preset=ProfileStore.LoadPreset(dialog.FileName);
            var presets=runtime.Profile.VehiclePresets.Where(v=>!(v.GameVersion==preset.GameVersion&&v.VehicleModel==preset.VehicleModel&&v.VehicleClass==preset.VehicleClass)).Append(preset).ToImmutableArray();
            var profile=runtime.Profile with{VehiclePresets=presets};ProfileStore.Save(profile);runtime.SetProfile(profile);
            Notify(preset.Verified?L.T("Text_922E914F31"):L.T("Text_D923F7EBF6"));
        }
        catch(Exception ex){Notify(L.Error(ex).ToString());}
    }
    private void ExportPresetClick(object sender,RoutedEventArgs e)
    {
        try
        {
            var frame=runtime.Current.Frame??throw L.InvalidOperation(L.M("Text_17B187FA8C"));
            var current=runtime.Profile.VehiclePresets.FirstOrDefault(v=>v.Matches(frame));
            var preset=new VehiclePreset(frame.GameVersion,frame.VehicleModel,frame.VehicleClass,false,"",current?.Parameters??runtime.Profile.DefaultParameters);
            var dialog=new Microsoft.Win32.SaveFileDialog{Filter=L.T("Text_B9A75A6A70"),FileName="vehicle-preset.json"};
            if(dialog.ShowDialog()==true){File.WriteAllText(dialog.FileName,System.Text.Json.JsonSerializer.Serialize(preset,ProfileStore.Json));Notify(L.T("Text_50F5845281"));}
        }
        catch(Exception ex){Notify(L.Error(ex).ToString());}
    }
    private void LogsClick(object sender,RoutedEventArgs e)
    {
        try{string path=Path.Combine(ProfileStore.DirectoryPath,"logs");Directory.CreateDirectory(path);Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}
        catch(Exception ex){Notify(L.Error(ex).ToString());}
    }
    public void FatalShutdown(string message){Notify(message);Close();}
    public void EmergencyStop()=>runtime.EmergencyStop();
    private async void OnClosing(object? sender,CancelEventArgs e)
    {
        if(closed)return;e.Cancel=true;if(closing)return;closing=true;timer.Stop();
        try{await runtime.DisposeAsync();}finally{closed=true;Close();}
    }
}
