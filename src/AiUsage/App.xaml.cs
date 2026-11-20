using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using AlData.AiUsage.Ui;

namespace AlData.AiUsage;

public partial class App : Application
{
    const string MutexName = @"Local\al-data-ai-usage";
    static readonly TimeSpan Handover = TimeSpan.FromSeconds(15);

    // Held for the life of the process; Windows frees it with the process.
    static Mutex? instance;
    Widget? widget;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
#if DEBUG
        // A Debug build keeps what the handler below swallows, in %TEMP%\al-data-ai-usage.log.
        Trace.Listeners.Add(new TextWriterTraceListener(Path.Combine(Path.GetTempPath(), "al-data-ai-usage.log")));
        Trace.AutoFlush = true;
#endif
        if (!SingleInstance())
        {
            Shutdown();
            return;
        }
        widget = new Widget();
        widget.Start();
        // From here on a bad draw or a provider bug must not take the widget down.
        DispatcherUnhandledException += (_, args) =>
        {
            Trace.WriteLine(args.Exception);
            args.Handled = true;
        };
    }

    /// <summary>One widget per session. A second launch (Start menu,
    /// startup task, a restart after an update) waits a little for the first to leave, then gives
    /// up quietly.</summary>
    static bool SingleInstance()
    {
        try
        {
            instance = new Mutex(true, MutexName, out var created);
            if (created)
                return true;
            try
            {
                return instance.WaitOne(Handover);
            }
            catch (AbandonedMutexException)
            {
                return true;   // the previous owner exited without releasing it: ours now
            }
        }
        catch (Exception)
        {
            return true;
        }
    }
}
