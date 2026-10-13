using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace Hatch.Services;

internal static partial class MyDayWidgetComServer
{
    private static readonly StrategyBasedComWrappers ComWrappers = new();
    private static readonly Guid ClassId = Guid.Parse("8F0AEEA3-75D7-4B2C-8BF0-4238E0042B16");
    private const string ServerArgument = "-RegisterProcessAsComServer";
    private const uint CoWaitInfinite = uint.MaxValue;
    private const uint CoWaitDispatchCalls = 0x8;
    private const uint CoWaitDispatchWindowMessages = 0x10;

    internal static bool IsActivation => Environment.GetCommandLineArgs()
        .Any(argument => argument.Equals(ServerArgument, StringComparison.OrdinalIgnoreCase));

    internal static void Run()
    {
        var factory = new WidgetProviderFactory();
        var factoryPointer = ComWrappers.GetOrCreateComInterfaceForObject(factory, CreateComInterfaceFlags.None);
        uint cookie;
        try
        {
            Marshal.ThrowExceptionForHR(CoRegisterClassObject(
                ClassId, factoryPointer, ClsctxLocalServer, RegclsMultipleUse, out cookie));
        }
        finally
        {
            Marshal.Release(factoryPointer);
        }

        try
        {
            var handles = new[]
            {
                MyDayWidgetProvider.EmptyWidgetListEvent.SafeWaitHandle.DangerousGetHandle()
            };
            // The class factory lives on WinUI's STA, so this wait must pump COM activation calls.
            Marshal.ThrowExceptionForHR(CoWaitForMultipleHandles(
                CoWaitDispatchCalls | CoWaitDispatchWindowMessages,
                CoWaitInfinite,
                1,
                handles,
                out _));
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
        IntPtr classFactory,
        uint context,
        uint flags,
        out uint registrationCookie);

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int CoRevokeClassObject(uint registrationCookie);

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int CoWaitForMultipleHandles(
        uint flags,
        uint timeout,
        uint handleCount,
        [In] IntPtr[] handles,
        out uint index);

    [GeneratedComInterface]
    [Guid("00000001-0000-0000-C000-000000000046")]
    internal partial interface IClassFactory
    {
        [PreserveSig]
        int CreateInstance(IntPtr outerUnknown, ref Guid interfaceId, out IntPtr instance);

        [PreserveSig]
        int LockServer([MarshalAs(UnmanagedType.Bool)] bool locked);
    }

    [GeneratedComClass]
    internal sealed partial class WidgetProviderFactory : IClassFactory
    {
        public int CreateInstance(IntPtr outerUnknown, ref Guid interfaceId, out IntPtr instance)
        {
            instance = IntPtr.Zero;
            if (outerUnknown != IntPtr.Zero)
                return unchecked((int)0x80040110); // CLASS_E_NOAGGREGATION

            if (interfaceId != typeof(IWidgetProvider).GUID &&
                interfaceId != Guid.Parse("00000000-0000-0000-C000-000000000046"))
                return unchecked((int)0x80004002); // E_NOINTERFACE

            var inspectable = MarshalInspectable<IWidgetProvider>.FromManaged(new MyDayWidgetProvider());
            try
            {
                return Marshal.QueryInterface(inspectable, in interfaceId, out instance);
            }
            finally
            {
                Marshal.Release(inspectable);
            }
        }

        public int LockServer(bool locked) => 0;
    }
}
