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
        InitializeComponent();App.RegisterEmergencyStop(runtime.EmergencyStop);
        try{runtime.SetProfile(ProfileStore.Load());}
        catch(Exception ex){Notify("配置读取失败，暂用默认值："+ex.Message);}
        ThemeManager.Apply(runtime.Profile.UiTheme);
        ThemeButton.Content=runtime.Profile.UiTheme=="Dark"?"浅色外观":"深色外观";
        BuildSettings();timer.Tick+=(_,_)=>Refresh();timer.Start();Closing+=OnClosing;runtime.Start();
    }
    private void Notify(string text)=>NoticeText.Text=text;
    private void ChangedNotice()=>Notify("设置已更改。点击“保存配置”后，下次启动会继续使用。");
    private void BuildSettings()
    {
        building=true;
        try
        {
            foreach(var panel in new[]{BrakeSettings,ThrottleSettings,ExtraTriggerSettings,RoadSettings,OtherGripSettings,SpatialSettings,AudioSettings})panel.Children.Clear();
            channelLabels.Clear();featureRows.Clear();
            AddFeature(ThrottleSettings,"Tc","TC 介入阻力","牵引力控制介入时，增加油门扳机阻力。");
            AddFeature(ExtraTriggerSettings,"ThrottleBase","R2 基础阻力","关闭“仅 TC 介入时增加阻力”后，可设置持续的油门阻力。");
            AddFeature(ExtraTriggerSettings,"Wheelspin","R2 空转提示","加速时车轮空转，通过 R2 脉冲提醒；需关闭上方的 TC 专用选项。");
            AddFeature(ExtraTriggerSettings,"Redline","R2 转速上限提示","接近引擎转速上限时，通过 R2 脉冲提醒；需关闭上方的 TC 专用选项。");
            AddFeature(BrakeSettings,"BrakeBase","L2 基础阻力","按下刹车时提供逐渐增强的阻力。");
            BrakeModeChoice.SelectedIndex=(int)runtime.Profile.BrakeMode;
            AddFeature(BrakeSettings,"Abs","ABS 提示","自动 GT3 使用旧版 ABS 脉冲；手动 ABS 模式使用介入阻力墙。");
            AddFeature(BrakeSettings,"Lock","临界制动预警","其他车型按接触区滑移预警，明显抱死输出 24 Hz 脉冲；GT3 保留旧版提示。");
            var brakeResponse=runtime.Profile.DefaultParameters.BrakeResponse;
            AddSetting("初始刹车上限参考",40,95,(int)(brakeResponse.InitialLimit*100),"未经车型标定的起点；系统会减去不确定性余量。",v=>SetBrakeResponse(runtime.Profile.DefaultParameters.BrakeResponse with{InitialLimit=v/100.0}),"%",BrakeSettings);
            AddSetting("L2 起始死区",0,30,(int)(brakeResponse.TravelDeadzone*100),"填入游戏中刹车轴起始死区；软件不读取真实扳机行程。",v=>SetBrakeResponse(runtime.Profile.DefaultParameters.BrakeResponse with{TravelDeadzone=v/100.0}),"%",BrakeSettings);
            AddSetting("L2 满刹行程",50,100,(int)(brakeResponse.TravelEnd*100),"达到游戏 100% 刹车时的扳机行程。",v=>SetBrakeResponse(runtime.Profile.DefaultParameters.BrakeResponse with{TravelEnd=v/100.0}),"%",BrakeSettings);
            AddSetting("刹车输入曲线指数",25,400,(int)(brakeResponse.InputExponent*100),"100 为线性，200 表示平方曲线；仅用于阻力位置映射。",v=>SetBrakeResponse(runtime.Profile.DefaultParameters.BrakeResponse with{InputExponent=v/100.0})," /100",BrakeSettings);
            AddFeature(RoadSettings,"Road","路面细节与冲击","通过左右握把感知路面纹理、颠簸和较大的垂向冲击。");
            AddFeature(RoadSettings,"Kerb","路肩反馈","压过路肩时，对应一侧的握把产生明显震动。");
            AddFeature(OtherGripSettings,"GripSlip","轮胎打滑","轮胎明显打滑时，握把震动提示抓地力下降。");
            AddFeature(OtherGripSettings,"Impact","碰撞","发生碰撞时，握把产生短促冲击。");
            AddFeature(OtherGripSettings,"Shift","升挡提示","升挡时，握把短促震动；关闭 TC 专用选项后，R2 也会产生脉冲。");
            AddFeature(OtherGripSettings,"Engine","引擎","震动随引擎转速变化；路面和路肩反馈优先。");
            AddSetting("路面灵敏度",25,200,runtime.Profile.RoadSensitivity,"100% 为起点；不影响 L2、R2 或引擎。",
                v=>runtime.SetProfile(runtime.Profile with{RoadSensitivity=v}));
            AddSetting("左右分离",0,100,runtime.Profile.SpatialSeparation,"数值越高，左右路面与路肩的交叉传递越少。",
                v=>runtime.SetProfile(runtime.Profile with{SpatialSeparation=v}));
            var audio=runtime.Profile.AudioHaptics;
            AudioModeCheck.IsChecked=audio.Enabled;SwapAudioSidesCheck.IsChecked=audio.SwapSides;
            KerbFallbackCheck.IsChecked=audio.DynamicKerbFallback;
            AddSetting("音圈总强度",0,100,audio.MasterStrength,"先检查左右握把震动，再调高；该滑块独立于 Windows 设备音量。",
                v=>SetAudio(runtime.Profile.AudioHaptics with{MasterStrength=v}),"%",AudioSettings);
            AddSetting("路面震动频率",25,180,audio.RoadFrequencyHz,"调节细碎路面震动的触感；频率越高，感觉越细密。",
                v=>SetAudio(runtime.Profile.AudioHaptics with{RoadFrequencyHz=v})," Hz",AudioSettings);
            AddSetting("路肩震动频率",40,220,audio.KerbFrequencyHz,"调节路肩震动的触感；压过路肩的节奏还会随车速变化。",
                v=>SetAudio(runtime.Profile.AudioHaptics with{KerbFrequencyHz=v})," Hz",AudioSettings);
            AddSetting("颠簸震动频率",20,100,audio.BumpFrequencyHz,"调节较大路面颠簸的触感。",
                v=>SetAudio(runtime.Profile.AudioHaptics with{BumpFrequencyHz=v})," Hz",AudioSettings);
            audioEndpointSignature="";RefreshAudioChoices();
            TcOnlyCheck.IsChecked=runtime.Profile.ThrottleMode==ThrottleFeedbackMode.TcOnly;
            UpdateModeControls();
        }
        finally{building=false;}
    }
    private void SetBrakeResponse(BrakeResponse response)=>runtime.SetProfile(runtime.Profile with{DefaultParameters=runtime.Profile.DefaultParameters with{BrakeResponse=response}});
    private void BrakeModeChanged(object sender,SelectionChangedEventArgs e)
    {
        if(building||!IsInitialized||BrakeModeChoice is null||runtime.RecordingPath is not null)return;
        if(BrakeModeChoice.SelectedIndex is <0 or >2)return;
        try{runtime.SetProfile(runtime.Profile with{BrakeMode=(BrakeFeedbackMode)BrakeModeChoice.SelectedIndex});ChangedNotice();}
        catch(Exception ex){Notify(ex.Message);}
    }
    private void AddFeature(StackPanel panel,string key,string name,string hint)
    {
        PropertyInfo property=typeof(Profile).GetProperty(key)!;
        var feature=(Feature)property.GetValue(runtime.Profile)!;
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
        AutomationProperties.SetName(slider,name+"强度");
        var value=new TextBlock{Text=feature.Strength.ToString(),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,14,0)};
        var check=new CheckBox{IsChecked=feature.Enabled,ToolTip="启用"+name,VerticalAlignment=VerticalAlignment.Center};
        AutomationProperties.SetName(check,"启用"+name);
        var diagnostic=new TextBlock{Text="等待驾驶",Style=(Style)FindResource("Muted"),HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,0,14,0)};
        Grid.SetColumn(slider,1);Grid.SetColumn(value,2);Grid.SetColumn(check,3);Grid.SetRowSpan(check,2);
        Grid.SetRow(diagnostic,1);Grid.SetColumn(diagnostic,1);Grid.SetColumnSpan(diagnostic,2);
        foreach(var element in new FrameworkElement[]{title,description,slider,value,check,diagnostic})row.Children.Add(element);
        panel.Children.Add(row);channelLabels[key]=diagnostic;featureRows[key]=row;
        void Changed()
        {
            if(building||runtime.RecordingPath is not null)return;
            try
            {
                var profile=runtime.Profile with{};property.SetValue(profile,new Feature(check.IsChecked==true,(int)slider.Value));
                runtime.SetProfile(profile);value.Text=((int)slider.Value).ToString();ChangedNotice();
            }
            catch(Exception ex){Notify(ex.Message);}
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
            catch(Exception ex){Notify(ex.Message);}
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
        catch(Exception ex){Notify(ex.Message);}
    }
    private void AudioDeviceChanged(object sender,SelectionChangedEventArgs e)
    {
        if(building||!IsInitialized||runtime.RecordingPath is not null||AudioDevicesCombo.SelectedItem is not AudioChoice selected)return;
        try{SetAudio(runtime.Profile.AudioHaptics with{EndpointId=selected.Id});ChangedNotice();}
        catch(Exception ex){Notify(ex.Message);}
    }
    private void RefreshAudioDevicesClick(object sender,RoutedEventArgs e)=>runtime.RefreshAudioDevices();
    private void SoundSettingsClick(object sender,RoutedEventArgs e)
    {try{Process.Start(new ProcessStartInfo("control.exe","mmsys.cpl,,0"){UseShellExecute=true});}catch(Exception ex){Notify(ex.Message);}}
    private void RefreshAudioChoices()
    {
        var devices=runtime.AudioEndpoints;string selected=runtime.Profile.AudioHaptics.EndpointId;
        string signature=selected+"|"+string.Join("|",devices.Select(d=>d.Id+":"+d.DisplayName));
        if(signature==audioEndpointSignature)return;
        audioEndpointSignature=signature;bool before=building;building=true;
        try
        {
            var choices=new List<AudioChoice>{new("","自动选择当前 USB 手柄")};
            choices.AddRange(devices.Select(d=>new AudioChoice(d.Id,d.DisplayName)));
            if(selected.Length>0&&!choices.Any(c=>c.Id==selected))choices.Add(new(selected,"已保存设备（当前未连接）"));
            AudioDevicesCombo.ItemsSource=choices;AudioDevicesCombo.SelectedValue=selected;
        }
        finally{building=before;}
    }
    private void UpdateModeControls()
    {
        bool only=runtime.Profile.ThrottleMode==ThrottleFeedbackMode.TcOnly;
        foreach(var key in new[]{"ThrottleBase","Wheelspin","Redline"})
            if(featureRows.TryGetValue(key,out var row)){row.IsEnabled=!only;row.Opacity=only?.52:1;}
        LegacyHint.Text=only?"当前 R2 仅在牵引力控制介入时增加阻力。关闭上方选项，可启用以下效果及升挡脉冲。":
            "当前可使用基础阻力、空转、转速上限及升挡提示；多个事件同时发生时，优先响应升挡和 TC。";
    }
    private void ThrottleModeChanged(object sender,RoutedEventArgs e)
    {
        if(building||!IsInitialized||runtime.RecordingPath is not null)return;
        try
        {
            runtime.SetProfile(runtime.Profile with{ThrottleMode=TcOnlyCheck.IsChecked==true?ThrottleFeedbackMode.TcOnly:ThrottleFeedbackMode.Legacy});
            UpdateModeControls();ChangedNotice();
        }
        catch(Exception ex){Notify(ex.Message);}
    }
    private void ThemeClick(object sender,RoutedEventArgs e)
    {
        if(runtime.RecordingPath is not null){Notify("停止录制后再更改主题，保持录制配置一致。");return;}
        var theme=runtime.Profile.UiTheme=="Dark"?"Light":"Dark";
        runtime.SetProfile(runtime.Profile with{UiTheme=theme});ThemeManager.Apply(theme);
        ThemeButton.Content=theme=="Dark"?"浅色外观":"深色外观";ChangedNotice();
    }
    private static int EncodedPeak(TriggerEffect e)
    {
        if(e.Mode==5)return 0;
        uint bits=(uint)(e.P2|(e.P3<<8)|(e.P4<<16)|(e.P5<<24));int mask=e.P0|(e.P1<<8);
        return Enumerable.Range(0,10).Max(i=>(mask&(1<<i))!=0?(int)((bits>>(i*3))&7)+1:0);
    }
    private void Refresh()
    {
        bool recording=runtime.RecordingPath is not null;
        TriggerSettingsRoot.IsEnabled=VibrationSettingsRoot.IsEnabled=!recording;
        ThemeButton.IsEnabled=!recording;RecordButton.Content=recording?"停止记录驾驶数据":"开始记录驾驶数据";
        var s=runtime.Current;var f=s.Frame;RefreshAudioChoices();
        var audio=s.Audio;bool audioMode=runtime.Profile.AudioHaptics.Enabled;
        AudioStatusText.Text=!audioMode?"当前使用普通握把震动。":audio is null?"正在连接手柄音频设备…":
            audio.Message+(audio.Ready?"\n已连接，可在“连接与工具”中检查左右握把震动。":"\n请检查 USB 连接、手柄播放设备及设备音量。");
        StateText.Text=s.State.StartsWith("Driving",StringComparison.Ordinal)?"驾驶中":
            s.State.StartsWith("DeviceReady",StringComparison.Ordinal)?"手柄检查中":
            s.State.StartsWith("Stale",StringComparison.Ordinal)?"车辆数据中断，反馈已暂停":
            s.State is "Paused"?"游戏已暂停":s.State is "非驾驶状态"?"等待进入驾驶":
            s.State is "WaitingForLMU"?"等待启动 LMU":s.State is "Disconnected"?"请通过 USB 连接 DualSense":s.State;
        DeviceChip.Text=s.DeviceConnected?"DualSense · USB 已连接":"DualSense · 未连接";
        CarText.Text=f is null?"通过 USB 连接 DualSense，进入 LMU 驾驶后自动启用反馈。":$"{f.VehicleModel}  /  {f.VehicleClass}  ·  {f.SpeedMps*3.6:F0} km/h  ·  {f.Rpm:F0} RPM";
        ConnectionText.Text=$"车辆数据 {s.TelemetryHz:F0} 次/秒   ·   扳机更新 {s.OutputHz:F0} 次/秒   ·   "+(audioMode?(audio?.Ready==true?"音圈已启动":"音圈未就绪"):"普通握把震动");
        ErrorText.Text=s.Error;ErrorBanner.Visibility=string.IsNullOrEmpty(s.Error)?Visibility.Collapsed:Visibility.Visible;
        ValidationText.Text=$"{s.Telemetry}\n手柄：{s.Device}\n参数：{s.ParameterSource}\n{s.LearningState}";
        int brake=EncodedPeak(s.Output.LeftTrigger),throttle=EncodedPeak(s.Output.RightTrigger);
        BrakeOutputText.Text=brake==0?"释放":(s.LeftTriggerSource switch{"Abs"=>"ABS 脉冲","AbsWall"=>"ABS 阻力墙","BrakeReference"=>"临界位置参考","BrakeLimit"=>"接近抱死","LockPulse"=>"抱死脉冲","Lock"=>"抱死提醒",_=>"基础阻力"})+$" · {brake} / 8";
        ThrottleOutputText.Text=throttle==0?"自由行程":s.RightTriggerSource=="Tc"?$"TC · {throttle} / 8":$"{throttle} / 8";
        BrakeMeter.Value=brake;ThrottleMeter.Value=throttle;
        LeftGripMeter.Maximum=RightGripMeter.Maximum=audioMode?1:255;
        if(audioMode)
        {
            var levels=audio?.Levels??default;
            LeftGripText.Text=audio?.Ready==true?$"{levels.LeftRms*100:F1}%":"音圈未就绪";
            RightGripText.Text=audio?.Ready==true?$"{levels.RightRms*100:F1}%":"音圈未就绪";
            LeftGripMeter.Value=levels.LeftRms;RightGripMeter.Value=levels.RightRms;
        }
        else
        {
            LeftGripText.Text=$"{s.Output.LeftActuator} / 255";RightGripText.Text=$"{s.Output.RightActuator} / 255";
            LeftGripMeter.Value=s.Output.LeftActuator;RightGripMeter.Value=s.Output.RightActuator;
        }
        foreach(var pair in channelLabels)
        {
            var feature=(Feature)typeof(Profile).GetProperty(pair.Key)!.GetValue(runtime.Profile)!;
            double level=(double)typeof(ChannelLevels).GetProperty(pair.Key)!.GetValue(s.Channels)!;
            bool bypass=runtime.Profile.ThrottleMode==ThrottleFeedbackMode.TcOnly&&(pair.Key is "ThrottleBase" or "Wheelspin" or "Redline");
            pair.Value.Text=bypass?"此模式不输出":feature.Gain==0?"已关闭":!s.AdvancedFeedbackEnabled?"等待驾驶":
                level>.001?$"效果 {level*100:F0}%":"待触发";
        }
        EventsText.Text=$"L2 [{s.LeftTriggerSource}] 0x{s.Output.LeftTrigger.Mode:X2}  /  R2 [{s.RightTriggerSource}] 0x{s.Output.RightTrigger.Mode:X2}\n"+
            $"ABS {s.Abs:F2}  抱死 {s.Lock:F2}  TC原始 {(f?.TcIntervening==true?1:0)}  TC包络 {s.Tc:F2}  空转 {s.Spin:F2}\n"+
            $"路面 L {s.Layers.RoadL:F2} R {s.Layers.RoadR:F2}  /  路肩 L {s.Layers.KerbL:F2} R {s.Layers.KerbR:F2}\n"+
            $"滑移 L {s.Layers.SlipL:F2} R {s.Layers.SlipR:F2}  /  碰撞 {Math.Max(s.Layers.ImpactL,s.Layers.ImpactR):F2}";
        WheelsText.Text=s.WheelDiagnostics+(f is null?"":"\n"+string.Join("\n",f.Wheels.Select((w,i)=>
            $"{new[]{"FL","FR","RL","RR"}[i]} surface={w.SurfaceType?.ToString()??"未知"} terrain={w.TerrainName} kerb={w.IsKerb} susp={w.SuspDeflectionM:F4}m tireCompression={w.TireVerticalDeflectionM*1000:F2}mm load={w.TireLoadN:F0}N")));
        PerformanceText.Text=$"间隔 P50/P95/P99：{s.IntervalP50:F2}/{s.IntervalP95:F2}/{s.IntervalP99:F2} ms\n"+
            $"快照→写入 P50/P95：{s.LatencyP50:F2}/{s.LatencyP95:F2} ms\n"+
            $"CPU {s.CpuPercent:F2}%  ·  分配 {s.AllocatedMBPerSecond:F2} MB/s  ·  内存 {s.WorkingSet/1048576.0:F1} MB\n"+
            $"音频设备：{audio?.EndpointId??"未连接"}\n音频格式：{audio?.Format??"未连接"}\n音频缓冲：{audio?.BufferMilliseconds??0:F1} ms · 已提交 {audio?.SubmittedFrames??0} 帧\n音频 L/R 峰值：{audio?.Levels.LeftPeak??0:F3}/{audio?.Levels.RightPeak??0:F3}\n"+
            $"录制丢帧 {s.Dropped}  ·  {runtime.RecordingPath??"未录制"}";
        DeviceTestsPanel.IsEnabled=f is not {Driving:true};
    }
    private void DeviceTestClick(object sender,RoutedEventArgs e)
    {try{runtime.RequestDeviceTest((string)((Button)sender).Tag);Notify("手柄检查已开始，1 秒后自动停止。");}catch(Exception ex){Notify(ex.Message);}}
    private void SaveClick(object sender,RoutedEventArgs e)
    {try{ProfileStore.Save(runtime.Profile);Notify("配置已保存。已有配置会在同一目录保留备份。");}catch(Exception ex){Notify("保存失败："+ex.Message);}}
    private void ResetClick(object sender,RoutedEventArgs e)
    {
        if(runtime.RecordingPath is not null){Notify("请先停止录制，再恢复参数。");return;}
        if(MessageBox.Show(this,"恢复全部反馈默认值？当前未保存的强度修改会丢失；已导入的车型预设会保留。","恢复默认值",MessageBoxButton.OKCancel,MessageBoxImage.Question)!=MessageBoxResult.OK)return;
        runtime.SetProfile(new(){VehiclePresets=runtime.Profile.VehiclePresets,RightGain=runtime.Profile.RightGain,UiTheme=runtime.Profile.UiTheme});
        BuildSettings();ChangedNotice();
    }
    private async void RecordClick(object sender,RoutedEventArgs e)
    {
        try{if(runtime.RecordingPath is null){runtime.StartRecording();Notify("正在记录驾驶数据；停止记录后可调整设置。");}else{await runtime.StopRecording();Notify("驾驶数据记录已停止，可点击“打开日志文件夹”查看。");}}
        catch(Exception ex){Notify("录制失败："+ex.Message);}
    }
    private void ImportPresetClick(object sender,RoutedEventArgs e)
    {
        try
        {
            if(runtime.RecordingPath is not null)throw new InvalidOperationException("请先停止录制");
            var dialog=new Microsoft.Win32.OpenFileDialog{Filter="车型预设 JSON|*.json"};if(dialog.ShowDialog()!=true)return;
            var preset=ProfileStore.LoadPreset(dialog.FileName);
            var presets=runtime.Profile.VehiclePresets.Where(v=>!(v.GameVersion==preset.GameVersion&&v.VehicleModel==preset.VehicleModel&&v.VehicleClass==preset.VehicleClass)).Append(preset).ToImmutableArray();
            var profile=runtime.Profile with{VehiclePresets=presets};ProfileStore.Save(profile);runtime.SetProfile(profile);
            Notify(preset.Verified?"车型预设已导入；版本、车型和类别匹配时自动启用。":"车型模板已保存；需完成参数验证并在模板中标记后才会启用。");
        }
        catch(Exception ex){Notify(ex.Message);}
    }
    private void ExportPresetClick(object sender,RoutedEventArgs e)
    {
        try
        {
            var frame=runtime.Current.Frame??throw new InvalidOperationException("读取到 LMU 车辆后再导出车型参数");
            var current=runtime.Profile.VehiclePresets.FirstOrDefault(v=>v.Matches(frame));
            var preset=new VehiclePreset(frame.GameVersion,frame.VehicleModel,frame.VehicleClass,false,"",current?.Parameters??runtime.Profile.DefaultParameters);
            var dialog=new Microsoft.Win32.SaveFileDialog{Filter="车型预设 JSON|*.json",FileName="vehicle-preset.json"};
            if(dialog.ShowDialog()==true){File.WriteAllText(dialog.FileName,System.Text.Json.JsonSerializer.Serialize(preset,ProfileStore.Json));Notify("车型模板已导出，可用于填写和分享该车型的参数。");}
        }
        catch(Exception ex){Notify(ex.Message);}
    }
    private void LogsClick(object sender,RoutedEventArgs e)
    {
        try{string path=Path.Combine(ProfileStore.DirectoryPath,"logs");Directory.CreateDirectory(path);Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}
        catch(Exception ex){Notify(ex.Message);}
    }
    public void FatalShutdown(string message){Notify(message);Close();}
    public void EmergencyStop()=>runtime.EmergencyStop();
    private async void OnClosing(object? sender,CancelEventArgs e)
    {
        if(closed)return;e.Cancel=true;if(closing)return;closing=true;timer.Stop();
        try{await runtime.DisposeAsync();}finally{closed=true;Close();}
    }
}
