using System.Windows;
using System.Threading;
using System.Runtime.InteropServices;
namespace Gather;
public partial class App : Application
{
    private Mutex? instance;
    private InstanceActivation? activation;
    private Mutex? setupMutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        SetCurrentProcessExplicitAppUserModelID("SCOTT0318.Moa.Desktop");
        AppPaths.Initialize(e.Args);
        if (!e.Args.Contains("--smoke-test"))
        {
            instance = new Mutex(true, "Local\\" + InstanceActivation.Name, out var first);
            if (!first) { InstanceActivation.Request().GetAwaiter().GetResult(); Shutdown(); return; }
            setupMutex = new Mutex(true, "Local\\Moa.Setup.Running");
            activation = new InstanceActivation(() => Dispatcher.BeginInvoke(() => (MainWindow as Gather.MainWindow)?.ShowMainWindow()));
        }
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e) { activation?.Dispose(); setupMutex?.Dispose(); instance?.Dispose(); base.OnExit(e); }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
