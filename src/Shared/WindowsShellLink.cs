using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace ApiNet.Platform;

// Source-generated COM interop also works in Native AOT; no WScript/dynamic COM dependency.
[SupportedOSPlatform("windows")]
internal sealed unsafe partial class WindowsShellLink : IDisposable
{
    private readonly IShellLinkW _link;
    private readonly bool _uninitialize;

    public WindowsShellLink()
    {
        var initialized = CoInitializeEx(0, 2);
        if (initialized < 0 && initialized != unchecked((int)0x80010106)) Marshal.ThrowExceptionForHR(initialized);
        _uninitialize = initialized >= 0;
        try
        {
            var classId = new Guid("00021401-0000-0000-C000-000000000046");
            var interfaceId = typeof(IShellLinkW).GUID;
            Marshal.ThrowExceptionForHR(CoCreateInstance(in classId, 0, 1, in interfaceId, out var instance));
            try { _link = UniqueComInterfaceMarshaller<IShellLinkW>.ConvertToManaged((void*)instance)!; }
            finally { Marshal.Release(instance); }
        }
        catch
        {
            if (_uninitialize) CoUninitialize();
            throw;
        }
    }

    public static WindowsShellLink Open(string path)
    {
        var link = new WindowsShellLink();
        try { Marshal.ThrowExceptionForHR(((IPersistFile)link._link).Load(path, 0)); return link; }
        catch { link.Dispose(); throw; }
    }

    public string TargetPath
    {
        get
        {
            var buffer = stackalloc char[32768];
            buffer[0] = '\0';
            Marshal.ThrowExceptionForHR(_link.GetPath(buffer, 32768, 0, 4));
            return new string(buffer);
        }
    }

    public (string Path, int Index) Icon
    {
        get
        {
            var buffer = stackalloc char[32768];
            buffer[0] = '\0';
            Marshal.ThrowExceptionForHR(_link.GetIconLocation(buffer, 32768, out var index));
            return (new string(buffer), index);
        }
    }

    public string Arguments
    {
        get
        {
            var buffer = stackalloc char[32768];
            buffer[0] = '\0';
            Marshal.ThrowExceptionForHR(_link.GetArguments(buffer, 32768));
            return new string(buffer);
        }
    }

    public void SetTarget(string path) => Marshal.ThrowExceptionForHR(_link.SetPath(path));
    public void SetArguments(string arguments) => Marshal.ThrowExceptionForHR(_link.SetArguments(arguments));
    public void SetIcon(string path, int index = 0) => Marshal.ThrowExceptionForHR(_link.SetIconLocation(path, index));
    public void Save(string path) => Marshal.ThrowExceptionForHR(((IPersistFile)_link).Save(path, 1));

    public void Dispose()
    {
        ((ComObject)(object)_link).FinalRelease();
        if (_uninitialize) CoUninitialize();
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint mode);
    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();
    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid classId, nint outer, uint context, in Guid interfaceId, out nint instance);
}

[GeneratedComInterface, Guid("000214F9-0000-0000-C000-000000000046")]
internal unsafe partial interface IShellLinkW
{
    [PreserveSig] int GetPath(char* path, int size, nint findData, uint flags);
    [PreserveSig] int GetIDList(out nint list);
    [PreserveSig] int SetIDList(nint list);
    [PreserveSig] int GetDescription(char* description, int size);
    [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
    [PreserveSig] int GetWorkingDirectory(char* directory, int size);
    [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
    [PreserveSig] int GetArguments(char* arguments, int size);
    [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
    [PreserveSig] int GetHotkey(out ushort hotkey);
    [PreserveSig] int SetHotkey(ushort hotkey);
    [PreserveSig] int GetShowCmd(out int command);
    [PreserveSig] int SetShowCmd(int command);
    [PreserveSig] int GetIconLocation(char* icon, int size, out int index);
    [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string icon, int index);
    [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
    [PreserveSig] int Resolve(nint window, uint flags);
    [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
}

[GeneratedComInterface, Guid("0000010B-0000-0000-C000-000000000046")]
internal partial interface IPersistFile
{
    [PreserveSig] int GetClassID(out Guid classId);
    [PreserveSig] int IsDirty();
    [PreserveSig] int Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);
    [PreserveSig] int Save([MarshalAs(UnmanagedType.LPWStr)] string file, int remember);
    [PreserveSig] int SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string file);
    [PreserveSig] int GetCurFile(out nint file);
}
