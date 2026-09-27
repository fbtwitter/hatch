using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace Hatch.Services;

internal static class MyDayWidgetComServer
{
    private static readonly Guid ClassId = Guid.Parse("8F0AEEA3-75D7-4B2C-8BF0-4238E0042B16");
    private const string ServerArgument = "-RegisterProcessAsComServer";

    internal static bool IsActivation => Environment.GetCommandLineArgs()
        .Any(argument => argument.Equals(ServerArgument, StringComparison.OrdinalIgnoreCase));

    internal static void Run()
    {
        Marshal.ThrowExceptionForHR(CoRegisterClassObject(
            ClassId,
            new WidgetProviderFactory<MyDayWidgetProvider>(),
            ClsctxLocalServer,
            RegclsMultipleUse,
            out var cookie));

        try
        {
            MyDayWidgetProvider.EmptyWidgetListEvent.WaitOne();
        }
        finally
        {
            _ = CoRevokeClassObject(cookie);
        }
    }

    private const uint ClsctxLocalServer = 0x4;
    private const uint RegclsMultipleUse = 0x1;

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int CoRegisterClassObject(
        [MarshalAs(UnmanagedType.LPStruct)] Guid classId,
        [MarshalAs(UnmanagedType.IUnknown)] object classFactory,
        uint context,
        uint flags,
        out uint registrationCookie);

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int CoRevokeClassObject(uint registrationCookie);

    [ComImport]
    [ComVisible(false)]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("00000001-0000-0000-C000-000000000046")]
    private interface IClassFactory
    {
        [PreserveSig]
        int CreateInstance(IntPtr outerUnknown, ref Guid interfaceId, out IntPtr instance);

        [PreserveSig]
        int LockServer(bool locked);
    }

    [ComVisible(true)]
    private sealed class WidgetProviderFactory<T> : IClassFactory
        where T : IWidgetProvider, new()
    {
        public int CreateInstance(IntPtr outerUnknown, ref Guid interfaceId, out IntPtr instance)
        {
            instance = IntPtr.Zero;
            if (outerUnknown != IntPtr.Zero)
                return unchecked((int)0x80040110); // CLASS_E_NOAGGREGATION

            if (interfaceId != typeof(IWidgetProvider).GUID &&
                interfaceId != Guid.Parse("00000000-0000-0000-C000-000000000046"))
                return unchecked((int)0x80004002); // E_NOINTERFACE

            instance = MarshalInspectable<IWidgetProvider>.FromManaged(new T());
            return 0;
        }

        public int LockServer(bool locked) => 0;
    }
}
