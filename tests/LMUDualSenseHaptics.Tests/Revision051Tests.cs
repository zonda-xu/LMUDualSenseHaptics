using System.Reflection;
using System.Runtime.InteropServices;

internal static class Revision051Tests
{
    // SDK mmdeviceapi.h, propsys.h and audioclient.h. Reflect the linked production
    // declarations, without activating Windows COM on the offline test host.
    public static void Run(Action<string, Action> check)
    {
        var identifiers = new Dictionary<string, string>
        {
            ["MMDeviceEnumerator"] = "BCDE0395-E52F-467C-8E3D-C4579291692E",
            ["IMMDeviceEnumerator"] = "A95664D2-9614-4F35-A746-DE8DB63617E6",
            ["IMMDeviceCollection"] = "0BD7A1BE-7A1A-44DB-8397-CC5392387B5E",
            ["IMMDevice"] = "D666063F-1587-4E43-81F1-B948E807363F",
            ["IPropertyStore"] = "886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99",
            ["IAudioClient"] = "1CB9AD4C-DBFA-4C32-B178-C2F568A703B2",
            ["IAudioRenderClient"] = "F294ACFC-3146-4483-A7BF-ADDCA7C260E2"
        };
        foreach (var (name, expected) in identifiers)
            check("WASAPI production COM identifier: " + name, () =>
            {
                var type = NativeType(name);
                Require(type.IsImport && type.GUID == Guid.Parse(expected));
                if (type.IsInterface)
                    Require(type.GetCustomAttribute<InterfaceTypeAttribute>()?.Value == ComInterfaceType.InterfaceIsIUnknown);
            });
        check("IMMDeviceCollection production vtable and marshalling contract", () =>
        {
            var type = NativeType("IMMDeviceCollection");
            var methods = type.GetMethods().OrderBy(m => m.MetadataToken).ToArray();
            Require(methods.Select(m => m.Name).SequenceEqual(new[] { "GetCount", "Item" }));
            Require(methods.All(m => m.ReturnType == typeof(int) &&
                (m.MethodImplementationFlags & MethodImplAttributes.PreserveSig) != 0));
            var count = methods[0].GetParameters();
            Require(count.Length == 1 && count[0].IsOut && count[0].ParameterType == typeof(uint).MakeByRefType());
            var item = methods[1].GetParameters();
            Require(item.Length == 2 && item[0].ParameterType == typeof(uint) && item[1].IsOut &&
                item[1].ParameterType == NativeType("IMMDevice").MakeByRefType());
            var output = NativeType("IMMDeviceEnumerator").GetMethod("EnumAudioEndpoints")!.GetParameters()[2];
            Require(output.IsOut && output.ParameterType == type.MakeByRefType());
        });
    }

    private static Type NativeType(string name) => typeof(Revision051Tests).Assembly.GetType(
        "LMUDualSenseHaptics.Hardware.WasapiNative+" + name, throwOnError: true)!;

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Windows SDK COM declaration mismatch");
    }
}
