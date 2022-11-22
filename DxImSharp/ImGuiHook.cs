using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpDX.Direct3D12;
using SharpDX.DXGI;
using MinHook;

namespace DxImSharp
{
    unsafe internal class ImGuiHook
    {
        [DllImport("kernel32")] static extern IntPtr LoadLibrary(String lpFileName);
        [DllImport("user32")] static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("cimgui")] static extern bool ImGui_ImplWin32_Init(IntPtr hwnd);
        [DllImport("cimgui")] static extern void ImGui_ImplWin32_NewFrame();
        [DllImport("cimgui")] static extern bool ImGui_ImplDX12_Init(IntPtr device, int num_frames_in_flight, IntPtr rtv_format, IntPtr cbv_srv_heap, IntPtr font_srv_cpu_desc_handle, long font_srv_gpu_desc_handle);
        [DllImport("cimgui")] static extern void ImGui_ImplDX12_NewFrame();
        [DllImport("cimgui")] static extern void ImGui_ImplDX12_RenderDrawData(IntPtr draw_data, IntPtr graphics_command_list);
        [DllImport("cimgui")] static extern void igNewFrame(); // NewFrame
        [DllImport("cimgui")] static extern void igEndFrame(); // EndFrame
        [DllImport("cimgui")] static extern void igShowDemoWindow(); // ShowDemoWindow
        [DllImport("cimgui")] static extern IntPtr igCreateContext(IntPtr fontAtlas); // CreateContext
        [DllImport("cimgui")] static extern void igStyleColorsDark(IntPtr style); // StyleColorsDark
        [DllImport("cimgui")] static extern void igRender(); // Render
        [DllImport("cimgui")] static extern IntPtr igGetDrawData(); // GetDrawData
        [DllImport("cimgui")] static extern ImGuiIO* igGetIO(); // GetDrawData

        delegate Int64 PresentDelegate(IntPtr pSwapChain, UInt32 SyncInterval, UInt32 Flags);
        static PresentDelegate OriginalPresent;
        delegate void ExecCmdListDelegate(IntPtr queue, uint NumCommandLists, IntPtr ppCommandLists);
        static ExecCmdListDelegate OriginalExecuteCommandLists;

        static CommandQueue g_pD3DCommandQueue;
        static Boolean g_Initialized = false;
        static IntPtr Window = IntPtr.Zero;
        delegate IntPtr WndProcDelegate(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);
        static IntPtr newWndProcPtr = IntPtr.Zero;
        static IntPtr oldWndProcPtr = IntPtr.Zero;
        static WndProcDelegate OriginalWndProc;
        static Delegate OriginalWndProcWinForms;
        static IntPtr NewWndProc(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam)
        {
            if (message == 0x201) igGetIO()->MouseDown[0] = 1; // WM_LBUTTONDOWN
            if (message == 0x202) igGetIO()->MouseDown[0] = 0; // WM_LBUTTONUP
            // todo add others
            //if (igGetIO().WantCaptureMouse) return IntPtr.Zero;
            if (OriginalWndProc != null) return OriginalWndProc(hWnd, message, wParam, lParam);
            return (IntPtr)OriginalWndProcWinForms.DynamicInvoke(hWnd, message, wParam, lParam);
        }
        static DescriptorHeap shaderResourceViewDescHeap;
        static DescriptorHeap renderTargetViewDescHeap;
        class FrameContext
        {
            public CommandAllocator command_allocator;
            public SharpDX.Direct3D12.Resource main_render_target_resource;
            public CpuDescriptorHandle main_render_target_descriptor;
        };
        static List<FrameContext> g_FrameContext = new List<FrameContext>();
        static GraphicsCommandList g_pD3DCommandList;
        static HookEngine engine = new HookEngine();
        public static void FindAndHookFuncs()
        {
            var device = new SharpDX.Direct3D12.Device(null, SharpDX.Direct3D.FeatureLevel.Level_12_0);
            var commandQueue = device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));
            var swapChainDesc = new SwapChainDescription()
            {
                BufferCount = 2,
                ModeDescription = new ModeDescription(100, 100, new Rational(60, 1), Format.R8G8B8A8_UNorm),
                Usage = Usage.RenderTargetOutput,
                SwapEffect = SwapEffect.FlipDiscard,
                OutputHandle = Process.GetCurrentProcess().MainWindowHandle,
                //Flags = SwapChainFlags.None,
                SampleDescription = new SampleDescription(1, 0),
                IsWindowed = true
            };
            using (var factory = new Factory4())
            using (var swapChain = new SwapChain(factory, commandQueue, swapChainDesc))
            {
                var swapChainTable = *(UInt64*)(UInt64)swapChain.NativePointer;
                var presentFunc = *(UInt64*)(UInt64)(swapChainTable + 8 * 8); // 8th func

                var commandQueueTable = *(UInt64*)(UInt64)commandQueue.NativePointer;
                var execCmdListFunc = *(UInt64*)(UInt64)(commandQueueTable + 10 * 8); // 10th func

                HookFuncs((IntPtr)presentFunc, (IntPtr)execCmdListFunc);
            }
        }
        public unsafe static void HookFuncs(IntPtr present, IntPtr execCmdList)
        {
            OriginalPresent = engine.CreateHook<PresentDelegate>((IntPtr)present, PresentOverride);
            OriginalExecuteCommandLists = engine.CreateHook<ExecCmdListDelegate>((IntPtr)execCmdList, ExecCmdListOverride);
            engine.EnableHooks();
        }
        static void ExecCmdListOverride(IntPtr pQueue, uint NumCommandLists, IntPtr ppCommandLists)
        {
            var queue = new CommandQueue(pQueue);
            if (g_pD3DCommandQueue == null && queue.Description.Type == CommandListType.Direct)
            {
                g_pD3DCommandQueue = queue;
                engine.DisableHook(OriginalExecuteCommandLists);
            }
            OriginalExecuteCommandLists(pQueue, NumCommandLists, ppCommandLists);
        }
        public static Action UI = new Action(() => igShowDemoWindow());
        static void GUI()
        {
            UI();
        }
        static Int64 PresentOverride(IntPtr pSwapChain, UInt32 SyncInterval, UInt32 Flags)
        {
            var swapChain = new SwapChain3(pSwapChain);
            if (g_pD3DCommandQueue == null) return OriginalPresent(pSwapChain, SyncInterval, Flags);
            if (!g_Initialized)
            {
                using (var d3dDevice = swapChain.GetDevice<SharpDX.Direct3D12.Device>())
                {
                    if (d3dDevice == null) return OriginalPresent(pSwapChain, SyncInterval, Flags);
                    Window = swapChain.Description.OutputHandle;
                    if (OriginalWndProc == null)
                    {
                        newWndProcPtr = Marshal.GetFunctionPointerForDelegate((WndProcDelegate)NewWndProc);
                        oldWndProcPtr = SetWindowLongPtrW(Window, -4, newWndProcPtr);
                        try
                        {
                            OriginalWndProc = Marshal.GetDelegateForFunctionPointer<WndProcDelegate>(oldWndProcPtr);
                        }
                        catch
                        {
                            OriginalWndProcWinForms = Marshal.GetDelegateForFunctionPointer(oldWndProcPtr, typeof(WndProcDelegate)); // managed delegate exists. System.Windows.Forms.NativeMethods+WndProc
                        }
                    }
                    var FrameBufferCount = swapChain.Description.BufferCount;
                    {
                        var desc = new DescriptorHeapDescription
                        {
                            Type = DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
                            DescriptorCount = FrameBufferCount,
                            Flags = DescriptorHeapFlags.ShaderVisible
                        };
                        shaderResourceViewDescHeap = d3dDevice.CreateDescriptorHeap(desc);
                        if (shaderResourceViewDescHeap == null) return OriginalPresent(pSwapChain, SyncInterval, Flags);
                    }
                    {
                        var desc = new DescriptorHeapDescription
                        {
                            Type = DescriptorHeapType.RenderTargetView,
                            DescriptorCount = FrameBufferCount,
                            Flags = DescriptorHeapFlags.None,
                            NodeMask = 1
                        };
                        renderTargetViewDescHeap = d3dDevice.CreateDescriptorHeap(desc);
                        if (renderTargetViewDescHeap == null) return OriginalPresent(pSwapChain, SyncInterval, Flags);

                        var rtvDescriptorSize = d3dDevice.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
                        var rtvHandle = renderTargetViewDescHeap.CPUDescriptorHandleForHeapStart;

                        for (var i = 0; i < FrameBufferCount; i++)
                        {
                            g_FrameContext.Add(new FrameContext
                            {
                                main_render_target_descriptor = rtvHandle,
                                main_render_target_resource = swapChain.GetBackBuffer<SharpDX.Direct3D12.Resource>(i),
                            });
                            var resource = swapChain.GetBackBuffer<SharpDX.Direct3D12.Resource>(i);
                            d3dDevice.CreateRenderTargetView(resource, null, rtvHandle);
                            rtvHandle.Ptr += rtvDescriptorSize;
                        }
                    }
                    {
                        var allocator = d3dDevice.CreateCommandAllocator(CommandListType.Direct);
                        if (allocator == null) return OriginalPresent(pSwapChain, SyncInterval, Flags);
                        for (var i = 0; i < FrameBufferCount; i++)
                        {
                            g_FrameContext[i].command_allocator = d3dDevice.CreateCommandAllocator(CommandListType.Direct);
                            if (g_FrameContext[i].command_allocator == null) return OriginalPresent(pSwapChain, SyncInterval, Flags);
                        }
                        g_pD3DCommandList = d3dDevice.CreateCommandList(0, CommandListType.Direct, g_FrameContext[0].command_allocator, null);
                        if (g_pD3DCommandList == null) return OriginalPresent(pSwapChain, SyncInterval, Flags);
                        g_pD3DCommandList.Close();
                    }
                    var context = igCreateContext(IntPtr.Zero);
                    ImGui_ImplWin32_Init(Window);
                    ImGui_ImplDX12_Init(((IntPtr)d3dDevice), FrameBufferCount, (IntPtr)28 /*DXGI_FORMAT_R8G8B8A8_UNORM*/, shaderResourceViewDescHeap.NativePointer, shaderResourceViewDescHeap.CPUDescriptorHandleForHeapStart.Ptr, shaderResourceViewDescHeap.GPUDescriptorHandleForHeapStart.Ptr);
                    g_Initialized = true;
                }
            }
            ImGui_ImplWin32_NewFrame();
            ImGui_ImplDX12_NewFrame();
            igNewFrame();
            UI();
            igEndFrame();
            var FrameBufferCountsfgn = swapChain.Description.BufferCount;
            var currentFrameContext = g_FrameContext[swapChain.CurrentBackBufferIndex];
            currentFrameContext.command_allocator.Reset();

            var barrier = new ResourceBarrier
            {
                Type = ResourceBarrierType.Transition,
                Flags = ResourceBarrierFlags.None,
                Transition = new ResourceTransitionBarrier(currentFrameContext.main_render_target_resource, -1, ResourceStates.Present, ResourceStates.RenderTarget)
            };

            g_pD3DCommandList.Reset(currentFrameContext.command_allocator, null);
            g_pD3DCommandList.ResourceBarrier(barrier);
            g_pD3DCommandList.SetRenderTargets(currentFrameContext.main_render_target_descriptor, null);
            g_pD3DCommandList.SetDescriptorHeaps(shaderResourceViewDescHeap);
            igRender();
            ImGui_ImplDX12_RenderDrawData(igGetDrawData(), g_pD3DCommandList.NativePointer);
            barrier.Transition = new ResourceTransitionBarrier
            {
                Subresource = barrier.Transition.Subresource,
                StateBefore = ResourceStates.RenderTarget,
                StateAfter = ResourceStates.Present
            };
            barrier.Transition = new ResourceTransitionBarrier(currentFrameContext.main_render_target_resource, -1, ResourceStates.RenderTarget, ResourceStates.Present);
            g_pD3DCommandList.ResourceBarrier(barrier);
            g_pD3DCommandList.Close();
            g_pD3DCommandQueue.ExecuteCommandList(g_pD3DCommandList);
            return OriginalPresent(pSwapChain, SyncInterval, Flags);
        }
    }
}
