using System.Runtime.InteropServices;

namespace Momiji.Interop.MMDeviceApi;
internal static class Libraries
{
    public const string Mmdevapi = "Mmdevapi.dll";
}

internal static class NativeMethods
{

    [DllImport(Libraries.Mmdevapi, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int ActivateAudioInterfaceAsync(
        [In][MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        [In] ref Guid riid,
        [In] nint /*PROPVARIANT*/ activationParams,
        [In] IActivateAudioInterfaceCompletionHandler completionHandler,
        [Out] out IActivateAudioInterfaceAsyncOperation activationOperation
    );

    [Guid("41d949ab-9862-444a-80f6-c261334da5eb"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), ComImport]
    internal interface IActivateAudioInterfaceCompletionHandler
    {
        [PreserveSig]
        int ActivateCompleted(
            [In] IActivateAudioInterfaceAsyncOperation activateOperation
        );
    }

    [Guid("72a22d78-cde4-431d-b8cc-843a71199b6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), ComImport]
    internal interface IActivateAudioInterfaceAsyncOperation
    {
        [PreserveSig]
        int GetActivateResult(
            [Out] out int activateResult,
            [Out][MarshalAs(UnmanagedType.IUnknown)] out object? activatedInterface
        );
    }


//Windows.Devices.Enumeration対照表
/*



 IMMDevice 
     Activate(
        _In_ REFIID iid,
        _In_ DWORD dwClsCtx,
        _In_opt_ PROPVARIANT *pActivationParams,
        _Out_  void** ppInterface) = 0;

    OpenPropertyStore(
        _In_ DWORD stgmAccess,
        _Out_ IPropertyStore **ppProperties) = 0;

    GetId(
        _Outptr_ LPWSTR *ppstrId) = 0;

    GetState(
        _Out_ DWORD *pdwState


IMMDeviceCollection 
    GetCount(
        _Out_ UINT *pcDevices) = 0;

    Item(
        _In_ UINT nDevice,
        _Out_ IMMDevice **ppDevice) = 0;

IMMDeviceEnumerator      
    EnumAudioEndpoints( 
        _In_  EDataFlow dataFlow,
        _In_  DWORD dwStateMask,
        _Out_  IMMDeviceCollection **ppDevices) = 0;

    GetDefaultAudioEndpoint( 
        _In_  EDataFlow dataFlow,
        _In_  ERole role,
        _Out_  IMMDevice **ppEndpoint) = 0;

    GetDevice( 
        _In_  LPCWSTR pwstrId,
        _Out_  IMMDevice **ppDevice) = 0;

    RegisterEndpointNotificationCallback( 
        _In_  IMMNotificationClient *pClient) = 0;

    UnregisterEndpointNotificationCallback( 
        _In_  IMMNotificationClient *pClient) = 0;


IMMEndpoint
     GetDataFlow( 
            _Out_  EDataFlow *pDataFlow) = 0;

 */




    internal enum EDataFlow : uint
    {
        eRender,
        eCapture,
        eAll,
        EDataFlow_enum_count
    };

    internal enum ERole : uint
    {
        eConsole,
        eMultimedia,
        eCommunications,
        ERole_enum_count
    };

    internal enum EndpointFormFactor : uint
    {
        RemoteNetworkDevice,
        Speakers,
        LineLevel,
        Headphones,
        Microphone,
        Headset,
        Handset,
        UnknownDigitalPassthrough,
        SPDIF,
        DigitalAudioDisplayDevice,
        UnknownFormFactor,
        EndpointFormFactor_enum_count
    };

    [Guid("7991eec9-7e89-4d85-8390-6c703cec60c0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMNotificationClient
    {
        [PreserveSig]
        int OnDeviceStateChanged(
            [In][MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId,
            [In] uint dwNewState
        );

        [PreserveSig]
        int OnDeviceAdded(
            [In][MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId
        );

        [PreserveSig]
        int OnDeviceRemoved(
            [In][MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId
        );

        [PreserveSig]
        int OnDefaultDeviceChanged(
            [In] EDataFlow flow,
            [In] ERole role,
            [In][MarshalAs(UnmanagedType.LPWStr)] string pwstrDefaultDeviceId
        );

        [PreserveSig]
        int OnPropertyValueChanged(
            [In][MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId,
            [In] nint/*PROPERTYKEY*/ key
        );
    }

}


