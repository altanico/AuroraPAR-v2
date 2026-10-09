using System.Configuration;
using System.Data;
using System.Windows;

namespace AuroraPAR
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // A crash leaves its error in the log of the coordination (coord-AuroraPAR.log), for the diagnosis.
            DispatcherUnhandledException += (s, args) => CoordinationLink.Log("ERROR " + args.Exception);
        }
    }

}
