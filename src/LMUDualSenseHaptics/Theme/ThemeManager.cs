using System.Windows;
using System.Windows.Media;
namespace LMUDualSenseHaptics.Theme;

internal static class ThemeManager
{
    public static void Apply(string name)
    {
        string[] keys=["Background","Surface","SurfaceLow","SurfaceHigh","OnSurface","OnSurfaceVariant",
            "Primary","OnPrimary","PrimaryContainer","OnPrimaryContainer","Outline","OutlineVariant",
            "Error","ErrorContainer","Warning","WarningContainer"];
        string[] light=["#EEF1EC","#FAFBF8","#E7ECE6","#DFE5DC","#364038","#58665C",
            "#466451","#FFFFFF","#D4E5D4","#304B36","#748277","#CFD7CC",
            "#914E46","#F3DCD7","#725D32","#ECE3CD"];
        string[] dark=["#282D2B","#303632","#343D36","#3B443D","#D8DFD7","#B4C0B6",
            "#B3D0B7","#263F2C","#425D48","#D1E7D2","#8C9C90","#566158",
            "#EAB9B0","#5A3F3C","#DCC79A","#494331"];
        var palette=name=="Dark"?dark:light;
        for(int i=0;i<keys.Length;i++)
        {
            Brush brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette[i]));
            // Honor Windows high-contrast colors rather than overriding accessibility.
            if(SystemParameters.HighContrast)
                brush=keys[i] is "Primary" or "Outline"?SystemColors.HighlightBrush:
                    keys[i] is "OnPrimary"?SystemColors.HighlightTextBrush:
                    keys[i].StartsWith("On",StringComparison.Ordinal)||keys[i] is "Error" or "Warning"?
                        SystemColors.WindowTextBrush:SystemColors.WindowBrush;
            Application.Current.Resources[keys[i]+"Brush"]=brush;
        }
    }
}
