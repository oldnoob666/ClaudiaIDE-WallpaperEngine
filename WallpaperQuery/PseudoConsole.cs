using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

// ConPTY provides the console APIs WE expects, without creating a visible
// console window or asking the user's default terminal application to launch.
internal static class PseudoConsole
{
    [StructLayout(LayoutKind.Sequential)] private struct Coord { public short X, Y; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string Reserved, Desktop, Title;
        public int X, Y, Width, Height, BufferX, BufferY, Fill, Flags;
        public short Show, ReservedSize;
        public IntPtr ReservedData, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo Startup; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process, Thread; public uint Id, ThreadId; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CreatePipe(out IntPtr read, out IntPtr write, IntPtr security, uint size);
    [DllImport("kernel32.dll")] private static extern int CreatePseudoConsole(Coord size, IntPtr input, IntPtr output, uint flags, out IntPtr console);
    [DllImport("kernel32.dll")] private static extern void ClosePseudoConsole(IntPtr console);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInfo process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr process, uint exitCode);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    internal static string Read(string executable, int monitor)
    {
        IntPtr inputRead = IntPtr.Zero, inputWrite = IntPtr.Zero, outputRead = IntPtr.Zero, outputWrite = IntPtr.Zero;
        IntPtr console = IntPtr.Zero, attributes = IntPtr.Zero;
        var info = new ProcessInfo();
        var attributesInitialized = false;
        try
        {
            if (!CreatePipe(out inputRead, out inputWrite, IntPtr.Zero, 0) || !CreatePipe(out outputRead, out outputWrite, IntPtr.Zero, 0)) throw new Win32Exception();
            var result = CreatePseudoConsole(new Coord { X = 4096, Y = 8 }, inputRead, outputWrite, 0, out console);
            Marshal.ThrowExceptionForHR(result);
            CloseHandle(inputRead); inputRead = IntPtr.Zero;
            CloseHandle(outputWrite); outputWrite = IntPtr.Zero;

            using (var pipe = new FileStream(new SafeFileHandle(outputRead, true), FileAccess.Read))
            using (var reader = new StreamReader(pipe, new UTF8Encoding(false), false))
            {
                outputRead = IntPtr.Zero; // owned by SafeFileHandle
                // Drain while the command runs: closing ConPTY flushes buffered VT output.
                var reading = Task.Run(() => reader.ReadToEnd());
                try
                {
                    var bytes = IntPtr.Zero;
                    InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref bytes);
                    attributes = Marshal.AllocHGlobal(bytes);
                    if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref bytes)) throw new Win32Exception();
                    attributesInitialized = true;
                    if (!UpdateProcThreadAttribute(attributes, 0, new IntPtr(0x00020016), console, new IntPtr(IntPtr.Size), IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception();
                    var startup = new StartupInfoEx { Attributes = attributes };
                    startup.Startup.Size = Marshal.SizeOf(typeof(StartupInfoEx));
                    var client = typeof(PseudoConsole).Assembly.Location;
                    var command = new StringBuilder("\"" + client + "\" --console-client \"" + executable + "\" " + monitor);
                    if (!CreateProcess(client, command, IntPtr.Zero, IntPtr.Zero, false, 0x00080000, IntPtr.Zero, Path.GetDirectoryName(executable), ref startup, out info)) throw new Win32Exception();
                    if (WaitForSingleObject(info.Process, 2500) != 0)
                    {
                        TerminateProcess(info.Process, 4);
                        throw new TimeoutException("Wallpaper query timed out");
                    }
                }
                finally
                {
                    ClosePseudoConsole(console); console = IntPtr.Zero;
                    CloseHandle(inputWrite); inputWrite = IntPtr.Zero;
                }
                if (!reading.Wait(500)) throw new TimeoutException("Console output did not close");
                return reading.Result;
            }
        }
        finally
        {
            if (info.Thread != IntPtr.Zero) CloseHandle(info.Thread);
            if (info.Process != IntPtr.Zero) CloseHandle(info.Process);
            if (attributesInitialized) DeleteProcThreadAttributeList(attributes);
            if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
            if (console != IntPtr.Zero) ClosePseudoConsole(console);
            foreach (var handle in new[] { inputRead, inputWrite, outputRead, outputWrite })
                if (handle != IntPtr.Zero) CloseHandle(handle);
        }
    }
}
