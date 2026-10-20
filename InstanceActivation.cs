using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Gather;
internal sealed class InstanceActivation : IDisposable
{
    internal static string Name => "Moa.Desktop." + Process.GetCurrentProcess().SessionId + "." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName)))[..16];
    private readonly CancellationTokenSource stop = new();
    private readonly Task listener;
    public InstanceActivation(Action activate) { listener = Listen(activate); }
    private async Task Listen(Action activate)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(Name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);
                using var read = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); read.CancelAfter(TimeSpan.FromSeconds(2));
                var command = new byte[1];
                if (await pipe.ReadAsync(command, read.Token).ConfigureAwait(false) == 1 && command[0] == 1) activate();
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        }
    }
    internal static async Task<bool> Request()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            if (GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid)) AllowSetForegroundWindow(pid);
            await pipe.WriteAsync(new byte[] { 1 }, timeout.Token).ConfigureAwait(false); return true;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException) { return false; }
    }
    public void Dispose() { stop.Cancel(); listener.GetAwaiter().GetResult(); stop.Dispose(); }
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AllowSetForegroundWindow(uint pid);
}
