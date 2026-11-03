using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Controls;
using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Config;
namespace LMUDualSenseHaptics;

public partial class MainWindow
{
    private void InitializeLanguage()
    {
        building=true;
        LanguageChoice.ItemsSource=L.Languages;
        LanguageChoice.SelectedValue=L.Language;
        ApplyLanguage();building=false;
    }
    private void ApplyLanguage()
    {
        foreach(var item in L.Resources(L.Language))Application.Current.Resources[item.Key]=item.Value;
        FlowDirection=L.Language=="ar"?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
        Language=XmlLanguage.GetLanguage(L.Language);
        CultureInfo.CurrentUICulture=L.Culture(L.Language);
    }
    private void LanguageChanged(object sender,SelectionChangedEventArgs e)
    {
        if(building||!IsInitialized||LanguageChoice.SelectedValue is not string code||code==L.Language)return;
        try
        {
            UiLanguageStore.Save(code);L.SetLanguage(code);ApplyLanguage();
            ThemeButton.Content=runtime.Profile.UiTheme=="Dark"?L.T("Text_02470E632F"):L.T("Text_46BAD67D06");
            RefreshTriggerScopes(true);Refresh();Notify(L.T("Text_86A8473D4D"));
        }
        catch(Exception ex)
        {
            building=true;LanguageChoice.SelectedValue=L.Language;building=false;
            Notify(L.T("Text_A4870A1A87")+L.Error(ex).ToString());
        }
    }
    private bool Confirm(string message,string title)
    {
        var dialog=new Window {Owner=this,Title=title,Width=480,SizeToContent=SizeToContent.Height,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,
            FlowDirection=FlowDirection,Language=Language,ShowInTaskbar=false};
        var panel=new StackPanel {Margin=new Thickness(24)};
        panel.Children.Add(new TextBlock {Text=message,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,20)});
        var actions=new WrapPanel {HorizontalAlignment=HorizontalAlignment.Right};
        var ok=new Button {Content=L.T("Text_FAC2A67AD8"),IsDefault=true,Style=(Style)FindResource("PrimaryButton")};
        var cancel=new Button {Content=L.T("Text_2CD0F3BE87"),IsCancel=true};
        System.Windows.Automation.AutomationProperties.SetAutomationId(ok,"ConfirmAccept");
        System.Windows.Automation.AutomationProperties.SetAutomationId(cancel,"ConfirmCancel");
        ok.Click+=(_,_)=>dialog.DialogResult=true;actions.Children.Add(ok);actions.Children.Add(cancel);
        panel.Children.Add(actions);dialog.Content=panel;return dialog.ShowDialog()==true;
    }
}
