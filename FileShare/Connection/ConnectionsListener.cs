using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Threading;
using System.Windows;

namespace FileShare
{
    /// <summary>
    /// This class will listen for transfer requests from other users
    /// </summary>
    class ConnectionsListener
    {
        /// <summary>
        /// The port
        /// </summary>
        private int Port;

        /// <summary>
        /// The address
        /// </summary>
        private IPAddress Address;

        /// <summary>
        /// Main window dispatcher, used to open a new window
        /// </summary>
        Dispatcher MainWindowDispatcher;

        /// <summary>
        /// The CancellationToken, used to cancel listening
        /// </summary>
        CancellationToken MainCancellationToken;

        /// <summary>
        /// The Constructor
        /// </summary>
        public ConnectionsListener()
        {
            MainWindowDispatcher = Application.Current.Dispatcher;
            Port = 2000;
            Address = IPAddress.IPv6Any;
            MainCancellationToken = (CancellationToken)Application.Current.Properties["MainCancellationToken"];
        }

        /// <summary>
        /// Start
        /// </summary>
        public void Start()
        {
            try
            {
                //------------------------------------
                //  Start the server
                //------------------------------------

                TcpListener Server = new TcpListener(IPAddress.IPv6Any, Port);
                Server.Start();

                //------------------------------------
                //  Actually listen for requests
                //------------------------------------

                using (CancellationTokenRegistration CTR = MainCancellationToken.Register(() => Server.Stop()))
                {
                    while (!MainCancellationToken.IsCancellationRequested)
                    {
                        TcpClient client = Server.AcceptTcpClient();

                        NewConnection(client);
                    }
                }
                
            }
            catch (Exception)
            {
                return;
            }
        }

        /// <summary>
        /// New connection found
        /// </summary>
        /// <param name="client">The client</param>
        private void NewConnection(TcpClient client)
        {
            //  If I am private, I am going to reject automatically,
            //  acting like a don't even exist
            if (((Settings)Application.Current.Properties["Settings"]).Ghost)
            {
                client.Close();
                return;
            }

            //  If I have to automatically accept all transfers, I automatically start the download.
            //  So I won't even ask the user to accept it.
            if(((Settings)Application.Current.Properties["Settings"]).AutoAccept)
            {
                List<Task> Transfers = (List<Task>)Application.Current.Properties["Transfers"];
                Transfers.Add(Task.Run(() =>
                {
                    Receive Receiver = new Receive(client);
                    if (!Receiver.ParseHello()) return;
                    Receiver.Start();

                    Receiver.Wait();
                }));

                return;
            }

            //------------------------------------
            //  Show the request
            //------------------------------------

            MainWindowDispatcher.InvokeAsync(() =>
            {
                ConnectionWindow w = new ConnectionWindow(client);

                //  UPDATE: show the window later, so we can first see if request is valid.
                /*w.Show();
                w.Activate();*/
            });
        }
    }
}
