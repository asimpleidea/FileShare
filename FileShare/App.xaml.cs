using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Threading;
using System.Text;
using System.Collections.ObjectModel;

namespace FileShare
{
    /**
     * When starting the application, some workers will be started.
     * 1) MulticastWorker will send every x seconds a keep alive packet, telling other users it is still alive
     *      and will parse other users' keep alive packets.
     * 2) SendToWorker listens for "Send To..." commands from current user
     * 3) ConnectionsListenerWorker will listen for connections from other peers.
     **/

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public Task MulticastWorker;
        public Task SendToWorker;
        public Task ConnectionsListenerWorker;

        //  The CancellationTokenSource
        //  This will tell all tasks that it's time to die.
        CancellationTokenSource Source = new CancellationTokenSource();
        CancellationToken Token;

        //  The taskbar icon
        System.Windows.Forms.NotifyIcon TaskbarIcon;

        /// <summary>
        /// App Constructor
        /// </summary>
        App()
        {
            //------------------------------------
            //  Application-wide properties
            //------------------------------------

            //  Denotes that the app is exiting, used to notify that we are exiting *NOT* going to taskbar
            Application.Current.Properties["Exiting"] = false;

            //  The cancellation token and source
            Application.Current.Properties["MainCancellationSource"] = Source;
            Token = Source.Token;
            Application.Current.Properties["MainCancellationToken"] = Token;

            //  Settings
            //  NOTE: I discovered the C# Application.Settings.Default when I was too far in programming.
            //  On next project, I'll be using that instead.
            Application.Current.Properties["Settings"] = new Settings();

            //  The Main window
            MainWindow mainWindow = new MainWindow();

            //------------------------------------
            //  Set up taskbar icon
            //------------------------------------

            TaskbarIcon = new System.Windows.Forms.NotifyIcon();
            TaskbarIcon.DoubleClick += (s, args) => mainWindow.ShowMainWindow();
            TaskbarIcon.Icon = FileShare.Properties.Resources.ShareIcon;
            TaskbarIcon.Visible = true;

            //------------------------------------
            //  Background workers
            //------------------------------------

            //  Note: Tasks will be waited later, right now they are in (almost) infinite loop,
            //  They just keep running until the app cancels them by calling MainCancellationTokenSource.Token.Cancel().

            MulticastWorker = Task.Run(() =>
            {
                MulticastManager mm = new MulticastManager();
            });

            SendToWorker = Task.Run(() =>
            {
                SendtoListener s = new SendtoListener();
                s.Listen();
            });

            ConnectionsListenerWorker = Task.Run(() =>
            {
                ConnectionsListener c = new ConnectionsListener();
                c.Start();
            });

            //------------------------------------
            //  The Transfers Window
            //------------------------------------

            //  The transferrers
            Application.Current.Properties["Transfers"] = new List<Task>();

            //  The list of progress bars
            Application.Current.Properties["TransferProgresses"] = new ObservableCollection<TransferProgress>();

            //  The Users
            //  NOTE: it's an ObservableCollection, so that when you want to send a file to someone,
            //  new users will be added triggering CollectionChanged event and showing them on the window.
            Application.Current.Properties["Users"] = new ObservableCollection<User>();

            //  Finally, start the Transfers Window and propagate it to everyone,
            //  so that they can show the window when a new connection is to be made.
            TransfersWindow t = new TransfersWindow();
            Application.Current.Properties["TransfersWindow"] = t;
        }

        /// <summary>
        /// The main entry point
        /// </summary>
        /// <param name="args">The command line arguments</param>
        [STAThread]
        static void Main(string[] args)
        {
            //------------------------------------
            //  Allowing only one instance
            //------------------------------------

            try
            {
                //  Create the mutex, other instances will not be able to create launch the app again
                using (Mutex mutex = new Mutex(true, @"PDS1_FileShare"))
                {
                    //  Wait 0 seconds, which means: just check if I could get the mutex
                    if (mutex.WaitOne(TimeSpan.Zero, true))
                    {
                        //  Got the mutex? It means it wasn't owned by anybody.
                        //  Let's start the application
                        App app = new App();
                        app.Run();

                        //  Wait for the workers to finish before dying (actually, at this point they are already dead.)
                        app.MulticastWorker.Wait();
                        app.SendToWorker.Wait();
                        app.ConnectionsListenerWorker.Wait();

                        //  "Why setting as null if it is disposed? The app is going to die anyway!"
                        //  Well, if you don't do it, the app's taskbar icon will stay there until you hover your mouse on it.
                        //  Weird, isn't it?
                        app.TaskbarIcon.Dispose();
                        app.TaskbarIcon = null;

                        //  I wrapped the mutex with using, do I really need to do this?
                        //  Doesn't .Disclose() already do this?
                        mutex.ReleaseMutex();
                    }

                    //  If I didn't get the lock, it means I am a second instance. 
                    //  NOTE: if you're here, it means that you are a new process, so we have to communicate
                    //  with the main process! I'll do that with a NamedPipe, see SendToWorker for details.
                    else
                    {
                        //  No args?
                        if(args.Length < 1)
                        {
                            MessageBox.Show("Error!", "Only one instance at a time is allowed.", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }

                        //  First arg is invalid?
                        if(args[0].Length < 1)
                        {
                            MessageBox.Show("Error!", "The file is invalid.", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }

                        //------------------------------------
                        //  Notify the SendToWorker
                        //------------------------------------

                        //  SendToWorker is waiting for the user to tell it to send something,
                        //  we are going to act as clients here.
                        using (System.IO.Pipes.NamedPipeClientStream client =
                            new System.IO.Pipes.NamedPipeClientStream(".", "pds_sendto", System.IO.Pipes.PipeDirection.Out))
                        {
                            client.Connect();

                            if(client.CanWrite)
                            {
                                byte[] arg = Encoding.Unicode.GetBytes(args[0]);
                                client.Write(arg, 0, arg.Length);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                MessageBox.Show("Error!", "An error occurred while trying to run the app.", MessageBoxButton.OK, MessageBoxImage.Error);
                //Debug.WriteLine(e.Message);
            }
        }
    }
}
