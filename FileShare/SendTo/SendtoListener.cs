using System;
using System.Text;
using System.Threading;
using System.IO.Pipes;
using System.Diagnostics;
using System.Windows.Threading;
using System.Windows;

namespace FileShare
{
    /// <summary>
    /// This class listens for transfer requests made by logged user.
    /// I am going to do it using NamedPipe
    /// </summary>
    class SendtoListener
    {
        /// <summary>
        /// The dispatcher of the main STA thread, used to open the SendTo Window
        /// </summary>
        private Dispatcher MainWindowDispatcher;

        /// <summary>
        /// The Cancellation Token, used to cancel operation
        /// </summary>
        private CancellationToken Token;

        /// <summary>
        /// The actual listener
        /// </summary>
        NamedPipeServerStream Server;

        /// <summary>
        /// Constructor
        /// </summary>
        public SendtoListener()
        {
            MainWindowDispatcher = Application.Current.Dispatcher;
            Token = (CancellationToken)Application.Current.Properties["MainCancellationToken"];
            Server = new NamedPipeServerStream("pds_sendto", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);         
        }

        /// <summary>
        /// Listen for transfer requests
        /// </summary>
        public void Listen()
        {
            using (CancellationTokenRegistration CTR = Token.Register(() => Server.Dispose()))
            {
                while (!Token.IsCancellationRequested)
                {
                    //  This will contain the path
                    byte[] path = new byte[1024];
                    int i = 0;

                    Debug.WriteLine("Going to wait for a message.");
                    
                    try
                    {
                        //  https://stackoverflow.com/questions/607872/what-is-a-good-way-to-shutdown-threads-blocked-on-namedpipeserverwaitforconnect/1191677
                        //  My first implementation was Server.WaitForConnection(),
                        //  but I realized that it was ignoring the cancellation.
                        //  I didn't like cancelling the Task without cancelling this first.
                        //  So, after lots of tries I decided to try to do it this way:
                        //  https://stackoverflow.com/questions/2700472/how-to-terminate-a-managed-thread-blocked-in-unmanaged-code/2700491#2700491
                        IAsyncResult asyncResult = Server.BeginWaitForConnection(null, null);

                        //  Wait for client to actually connect
                        if (asyncResult.AsyncWaitHandle.WaitOne())
                        {
                            Server.EndWaitForConnection(asyncResult);
                            // success
                            Debug.WriteLine("Waited, now we're in.");

                            if (Server.CanRead)
                            {
                                int c;
                                while (i < 1024)
                                {
                                    //  Prefered using ReadByte as I don't know exactly how many bytes I'll have to read
                                    c = Server.ReadByte();

                                    //  Make it end gracefully (c == -1 means end of stream)
                                    if (c == -1) i = 1024;
                                    else path[i++] = (byte)c;
                                }

                                Debug.WriteLine("read " + Encoding.Unicode.GetString(path));

                                /*
                                * Why trimming \0?? That's just the c-style string terminator, right?
                                * Well, while testing, I noticed that FileInfo (in Send.cs) was *always* throwing
                                * an ArgumentException, stating that there were invalid characters in the path.
                                * It was really weird, every string I passed to it was correct! 
                                * I could even open the file in Windows Explorer, just by copy-pasting!
                                * Well, after *ONE FUCKING HOUR DEBUGGING*, I discovered that FileInfo
                                * doesn't like the \0 after the string, because it parses it as a valid character.
                                * THANKS.
                                */
                                OpenWindow(Encoding.Unicode.GetString(path).Trim('\0'));

                            }
                        }

                        //  Disconnect the server and wait for other requests
                        Server.Disconnect();
                    }
                    catch (Exception /*e*/)
                    {
                        Debug.WriteLine("PipeListener ended");
                    }
                }
            }     
        }

        /// <summary>
        /// Opens the Send To Window
        /// </summary>
        /// <param name="path">The path</param>
        private void OpenWindow(string path)
        {
            //  You cannot open a new WPF window from a thread different from a different thread, as it must be STA.
            //  So I call the Main Window Dispatcher, which maintains a queue of jobs to do and executes them.
            MainWindowDispatcher.InvokeAsync(() =>
            {
                SendToWindow w = new SendToWindow(path)
                {
                    Topmost = true,
                    WindowState = WindowState.Normal
                };
                w.Show();
                w.Activate();                
                w.Topmost = false;
                
            });            
        }
    }
}
