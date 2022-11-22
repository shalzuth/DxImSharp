using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace DxImSharp
{
    internal unsafe class Program
    {
        unsafe static void Main()
        {
            if (Process.GetCurrentProcess().ProcessName == typeof(Program).Namespace)
            {
                var d3d12AppName = "d3d12test";
                var appDir = Path.GetDirectoryName(Process.GetProcessesByName(d3d12AppName)[0].MainModule.FileName);
                // need a better solution for cimgui.dll, maybe manual map, or pass in path through clr
                using (var cimguiDll = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("DxImSharp.cimgui.dll"))
                using (var memoryStream = new MemoryStream())
                {
                    cimguiDll.CopyTo(memoryStream);
                    var cimguiDllBytes = memoryStream.ToArray();
                    if (!File.Exists(appDir + @"\cimgui.dll") || !File.ReadAllBytes(appDir + @"\cimgui.dll").SequenceEqual(cimguiDllBytes)) File.WriteAllBytes(appDir + @"\cimgui.dll", cimguiDllBytes);
                }
                NativeNetSharp.NativeNetSharp.Inject(d3d12AppName, File.ReadAllBytes(System.Reflection.Assembly.GetEntryAssembly().Location));
            }
            else
            {
                NativeNetSharp.NativeNetSharp.AllocConsole();
                ImGuiHook.FindAndHookFuncs();
                Console.WriteLine("loaded");
                Console.Read();
            }
        }
    }
}
