using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace FileShare
{
    /// <summary>
    /// This class sends a file/folder to the other peer
    /// </summary>
    public class Send : Transfer
    {
        /// <summary>
        /// The Endpoint
        /// </summary>
        private IPEndPoint EndPoint;

        /// <summary>
        /// Reads data
        /// </summary>
        private Task Loader;

        /// <summary>
        /// Sends data
        /// </summary>
        private Task Sender;

        /// <summary>
        /// The tcp client (socket)
        /// </summary>
        TcpClient Client;

        /// <summary>
        /// The responses type
        /// </summary>
        enum Response { ACCEPTED, REJECTED, CANCELED, UNRECOGNIZED, UKNOWN };

        /// <summary>
        /// The kind of path 
        /// </summary>
        enum PathType { DIRECTORY, FILE, UKNOWN };

        /// <summary>
        /// What I am sending
        /// </summary>
        PathType SendType;

        /// <summary>
        /// The files I am sending
        /// </summary>
        /// <remarks>If I am sending just one file, Files.Count = 1.</remarks>
        IEnumerable<string> Files;

        /// <summary>
        /// The origin path that the *RECEIVER* must set.
        /// </summary>
        /// <example>file1, or folder1/file1</example>
        private string Origin = String.Empty;

        /// <summary>
        /// Shortcut for the index in which the origin is found in the filepath
        /// </summary>
        /// <example>If I am sending the folder C:\Project\Pictures, its value corresponds with the P of Pictures</example>
        private int OriginIndex = 0;

        /// <summary>
        /// The progress (for the View)
        /// </summary>
        TransferProgress Progress;

        /// <summary>
        /// The user I am sending this to
        /// </summary>
        User Receiver = null;

        /// <summary>
        /// Flag if I need to stop sending
        /// </summary>
        private bool StopSending = false;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="path">The file path</param>
        /// <param name="user">The user I am sending it to</param>
        public Send(string path, User user) : base()
        {
            FilePath = path;
            Receiver = new User(user);
            //EndPoint = new IPEndPoint(IPAddress.IPv6Loopback, port);
            EndPoint = new IPEndPoint(user.IP, Port);
            SendType = SetPathType();

            // If I am sending a file, I am just going to add 1 file to the IEnumerable
            if (SendType == PathType.FILE)
            {
                Files = Enumerable.Repeat<string>(FilePath, 1);
            }

            // Get files inside the directory
            //  NOTE: EnumerateFiles returns an IEnumerable<string> that can be *LAZILY* looped
            //  So, no worries for directories with lots of files
            if (SendType == PathType.DIRECTORY)
            {
                Files = Directory.EnumerateFiles(FilePath, "*", SearchOption.AllDirectories);
                DirectoryInfo DI = new DirectoryInfo(FilePath);

                //  Get the name of this directory, so I can tell the sender the name of the directory to create
                Origin += DI.Name;

                //  All files will be inside this folder, so their path will all share the same path to this folder.
                //  So I save CPU by calculating in advance its position.
                //  This means that when getting the filename, you should ignore OriginIndex characters before it.
                OriginIndex = FilePath.LastIndexOf(Origin) + Origin.Length + 1;
                Origin += "/";
            }
        }

        /// <summary>
        /// Start transferring
        /// </summary>
        public override void Start()
        {
            //  Before getting here, are there any errors?
            if (SendType == PathType.UKNOWN) return;

            Worker = Task.Run(() =>
            {
                Progress = new TransferProgress
                {
                    UserName = Receiver.Name,
                    UserPicture = Receiver.PicBytes,
                    FromOrTo = "to",
                    Parent = this
                };

                //  Add new Progress Bar                    
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ((ObservableCollection<TransferProgress>)Application.Current.Properties["TransferProgresses"]).Insert(0, Progress);
                });

                using (Client = new TcpClient(AddressFamily.InterNetworkV6))
                {
                    //------------------------------------
                    //  Try To Connect
                    //------------------------------------

                    if (!Connect())
                    {
                        //  Set Error Message as not responding
                        return;
                    }

                    //------------------------------------
                    //  Send the "Hello" message
                    //------------------------------------

                    using (Stream = Client.GetStream())
                    {
                        Stream.Flush();

                        //  At this point, I have already notified the receiver that I'm goin to send a directory.
                        //  So from now on, I'm going to send files, not directories
                        SendType = PathType.FILE;

                        //  Tells if you need to create a new progress bar
                        //  The first time, you won't have to, but for subsequent files yes you do.
                        bool _progress = true;

                        //  Loop through the files
                        foreach (string file in Files)
                        {
                            FilePath = file;
                            Work(_progress);

                            _progress = false;
                            if (StopSending) break;
                        }

                        Stop();

                    }   //  Dispose the stream

                    Client.Close();
                }   //  Dispose the client
            });

            Worker.Wait();
            Worker.Dispose();
            Worker = null;
        }

        /// <summary>
        /// Actually do stuff
        /// </summary>
        /// <param name="UseExistingProgress">If a new progress bar should be created</param>
        private void Work(bool UseExistingProgress = false)
        {
            if(!UseExistingProgress)
            {
                Progress = new TransferProgress
                {
                    UserName = Receiver.Name,
                    UserPicture = Receiver.PicBytes,
                    FromOrTo = "to",
                    Parent = this
                };

                //  Add new Progress Bar                    
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ((ObservableCollection<TransferProgress>)Application.Current.Properties["TransferProgresses"]).Insert(0, Progress);
                });
            }            

            //  Check the file
            if (!GetFileData()) return;

            //  Update the progress bar with the new data just obtained
            Progress.FileName = FileName;
            Progress.Maximum = FileSize;

            //  Don't even start transferring if user already cancelled
            if (!MainCancellationToken.IsCancellationRequested)
            {
                //  Set up the cancellation token
                AbortTransfer = new CancellationTokenSource();

                //  Send File data
                Response response = SendHello();
                if (response != Response.ACCEPTED)
                {
                    if (response == Response.REJECTED) StopSending = true;

                    Progress.CurrentActivity = "Request rejected";
                    Progress.IsError = true;
                    return;
                }

                if (AbortTransfer.Token.IsCancellationRequested)
                {
                    Progress.CurrentActivity = "Operation Cancelled.";
                    Progress.IsError = true;

                    //  Useless, but just to keep things clean
                    StopSending = true;
                    return;
                }

                //  If the main token (the one thrown by the main application) requests cancellation,
                //  then cancel the trasfer too.
                using (CancellationTokenRegistration ctr = MainCancellationToken.Register(() => Cancel()))
                {
                    //------------------------------------
                    //  Load the file
                    //------------------------------------

                    Loader = Task.Run(() => { Load(); }, AbortTransfer.Token);

                    //------------------------------------
                    //  Send the file
                    //------------------------------------

                    Sender = Task.Run(() => { Transmit(); }, AbortTransfer.Token);

                    Progress.CurrentActivity = "Transferring...";

                    //  Wait for them to finish (Or that the user cancels them).
                    //  NOTE: I can't wrap the Task definition in using(),
                    //  because I need to wait for them to finish! I can't dispose them earlier!
                    Sender.Wait();
                    Loader.Wait();

                    //  Dispose the tasks
                    Sender.Dispose();
                    Loader.Dispose();

                    if (!Progress.IsError)
                    {
                        Progress.CurrentActivity = "Transfer completed";
                        Progress.TextColor = "Green";
                    }
                }

                //  Dispose it;
                AbortTransfer.Dispose();

                //  Clean up
                //  If you're here, it means that the sender and loader have exited, so no need to lock.
                Buffer.Clear();
            }
        }

        /// <summary>
        /// Send a stop message
        /// </summary>
        private void Stop()
        {
            try
            {
                //  Send stop
                //  No need to check for a cancellation here, this just serves the purpose of notifying that I want to finish.
                byte[] data = Encoding.UTF8.GetBytes("ENDOC"); // END OF CONNECTION
                Stream.Write(data, 0, data.Length);
            }
            catch (Exception)
            {
                return;
            }

            /**
             * SECURITY IMPLICATIONS
             * 
             * Since this protocol is not authenticated, an attacker might send a ENDOC message
             * by spoofing their IP address to the receiver, ending the communication between the two peers.
             * 
             * POSSIBILE SOLUTION
             * 
             * 1) Use a symmetric or asymmetric authentication:
             *      Basically the same as with HELLO
             * 2) Send the number of files a priori:
             *      When I receive an ENDOC before completing all files, I know it might have come from a different sender.
             *      But this might be a problem with folders with lot of files (I need to wait for EnumerateFiles to finish)
             * 3) Use TSL when sending message:
             *      Basically the same as with HELLO
             */
        }

        /// <summary>
        /// Connect with the user
        /// </summary>
        /// <returns>true if success.</returns>
        private bool Connect()
        {
            //------------------------------------
            //  Connect
            //------------------------------------

            Progress.CurrentActivity = "Contacting user...";

            try
            {
                //  Connect and wait for a reply for 30 seconds
                Task Connect = Client.ConnectAsync(EndPoint.Address, EndPoint.Port);
                Connect.Wait(30 * 1000, MainCancellationToken);

                //  If you're here it means that it went ok
                return true;
            }
            catch (Exception e)
            {
                if (e is SocketException) Progress.CurrentActivity = "User seems not to be online";
                if (e is OperationCanceledException) Progress.CurrentActivity = "Time out or operation cancelled...";
                Progress.IsError = true;

                //  If you're here it means that either time ran out OR no one's listening at that port.
                return false;
            }
        }

        /// <summary>
        /// Gets what this path is 
        /// </summary>
        /// <returns>The file type</returns>
        private PathType SetPathType()
        {
            if (Directory.Exists(FilePath)) return PathType.DIRECTORY;
            else
            {
                if (File.Exists(FilePath)) return PathType.FILE;

                Progress.CurrentActivity = "Could not identify file type";
                Progress.IsError = true;
                return PathType.UKNOWN;
            }
        }

        /// <summary>
        /// Get file's size, extension and name.
        /// </summary>
        /// <returns>true on success.</returns>
        private bool GetFileData()
        {
            try
            {
                //  Get file's data
                FileInfo f = new FileInfo(FilePath);
                FileSize = f.Length;
                FileExtension = Path.GetExtension(FilePath).TrimStart('.');

                if (Origin.Equals(String.Empty)) FileName = Path.GetFileNameWithoutExtension(FilePath);
                else FileName = Origin + (FilePath.Substring(OriginIndex)).Replace("." + FileExtension, "").Replace("\\", "/");

                return true;
            }
            catch (Exception)
            {
                Progress.CurrentActivity = "File not found or permission denied";
                StopSending = true;
                return false;
            }
        }

        /// <summary>
        /// Load bytes from the disk
        /// </summary>
        private void Load()
        {
            //------------------------------------
            //  Init
            //------------------------------------

            long read = 0;

            //  https://stackoverflow.com/questions/34325034/how-do-i-cancel-a-filestream-readasync-request/34325233#34325233
            //  If no IsAsync is specified, readasync does not really implement cancellation.
            //  NOTE: I started the project with the name FileShare, without knowing it was included in System.IO...
            using (FileStream file = new FileStream(FilePath, FileMode.Open, FileAccess.Read, System.IO.FileShare.Read, 4096))
            {
                //------------------------------------
                //  Load data
                //------------------------------------
                while (read < FileSize && !AbortTransfer.Token.IsCancellationRequested)
                {
                    //  How many bytes you should read
                    //  Casted to int as the maximum will always be 4096, which is the defaults size of Read btw
                    int get = (int)(FileSize - read < 4096 ? (FileSize - read) : 4096);
                    byte[] _data = new byte[get];
                    byte[] data;
                    int _read = 0;

                    try
                    {
                        //  Actually load data
                        //  It seems that the overload ReadAsync(byte[], int32, int32, CancellationToken) 
                        //  doesn't really support cancellation. So I made it with a Wait(CancellationToken)
                        using (Task<int> r = file.ReadAsync(_data, 0, get))
                        {
                            r.Wait(AbortTransfer.Token);
                            //  Completed?
                            _read = r.Result;

                            //  As per documentation, ReadAsync reads 0 bytes if EOF is reached,
                            //  I'm checking it anyway...
                            if (_read == 0) throw new EndOfStreamException();
                            data = new byte[_read];
                            Array.Copy(_data, data, _read);
                        }
                    }
                    catch (Exception)
                    {
                        //  Notify the Sender that we must exit
                        lock (Buffer)
                        {
                            //  If you're here, it means that you were able to get the lock either
                            //  because the Sender is sending or because it's sleeping.
                            //  In the latter case I *MUST* tell it to wake up and exit
                            Monitor.Pulse(Buffer);

                            //  Set the cancellation token.
                            //  I *MUST* set the cancellation token *WHILE* still in lock,
                            //  so that i can cancel the operation while the sender is still blocked,
                            //  ensuring that it will catch the cancellation.
                            AbortTransfer.Cancel();
                        }

                        return;
                    }

                    //------------------------------------
                    //  Store data on buffer
                    //------------------------------------

                    lock (Buffer)
                    {
                        //  I only allow a maximum number of 5 elements,
                        //  so the >= is useless here.
                        if (Buffer.Count >= 5)
                        {
                            /**
                             * Note: I have to wrap Monitor.Wait in two if(Token.CancellationRequested)
                             * because I have to check if task has been cancelled!.
                             * The first one is because user might have interrupted it OR the sender had an exception.
                             * The second one is to check if I have been awoken because there is data OR I have to quit.
                             **/

                            if (!AbortTransfer.Token.IsCancellationRequested)
                            {                
                                Monitor.Wait(Buffer);
                            }

                            //  Did you wake me up because there's data or because we have to exit?
                            //  Doing break, so it gracefully exits the loop
                            if (AbortTransfer.Token.IsCancellationRequested) break;
                        }

                        //  Put data into buffer
                        Buffer.Enqueue(data);

                        //  Wake up the Sender if it was sleeping (which is probably so the first time)
                        Monitor.Pulse(Buffer);
                    }

                    //  Update how many data I have sent
                    read += _read;
                }

                //------------------------------------
                //  Send Digest
                //------------------------------------

                if (read == FileSize && !AbortTransfer.Token.IsCancellationRequested) ComputeDigest(file);
            }
        }

        /// <summary>
        /// Compute the Digest
        /// </summary>
        /// <param name="f">The filestream</param>
        private void ComputeDigest(FileStream f)
        {
            Progress.CurrentActivity = "Computing SHA256...";

            using (SHA256 sha = SHA256Managed.Create())
            {
                f.Position = 0;
                byte[] Digest = null;

                //  Doing Like this because ComputeHash is *NOT* cancellable (I should close the stream to do it)
                Task<byte[]> Compute = Task.Run(() =>
                {
                    try
                    {
                        byte[] _digest = sha.ComputeHash(f);
                        return Task.FromResult<byte[]>(_digest);
                    }
                    catch (Exception)
                    {
                        return Task.FromResult<byte[]>(null);
                    }
                });

                try
                {
                    //  Wait for the digest to finish
                    Compute.Wait(AbortTransfer.Token);

                    //  If you're here, it means that the task ran to completion
                    //  Set Digest and set DigestReady as true, it will be set false later if necessary
                    Digest = Compute.Result;
                    Buffer.Enqueue(Digest);

                    //  If an error happened...
                    if (Digest == null || Digest.Length != 32)
                    {
                        Progress.CurrentActivity = "An error occurred while trying to compute SHA256";
                        Progress.IsError = true;
                        Cancel();
                    }
                }
                catch (Exception)
                {
                    Progress.CurrentActivity = "An error occurred while trying to compute SHA256";
                    Progress.IsError = true;
                    Cancel();
                }
                finally
                {
                    //  Wake up the sender
                    lock (this)
                    {
                        Monitor.Pulse(this);
                    }
                }
            }
        }

        /// <summary>
        /// Send the file
        /// </summary>
        private void Transmit()
        {
            //------------------------------------
            //  Init
            //------------------------------------

            long written = 0;

            //------------------------------------
            //  Get the file data
            //------------------------------------

            Progress.CurrentActivity = "Transferring file...";

            while (written < FileSize && !AbortTransfer.IsCancellationRequested)
            {
                //  How much should I write
                int get = (int)(FileSize - written < 4096 ? (FileSize - written) : 4096);
                byte[] data = new byte[get];

                //  Try to acquire the buffer. I can't write if nothing's there to write yet, man.
                lock (Buffer)
                {
                    if (Buffer.Count == 0)
                    {
                        /**
                         * Read considerations I wrote for Monitor.Wait in Loader's code
                         **/
                        if (!AbortTransfer.Token.IsCancellationRequested)
                        {
                            Monitor.Wait(Buffer);
                        }

                        //  Did you wake me up because there's data or because we have to exit?
                        //  Doing break, so it gracefully exits the method
                        if (AbortTransfer.Token.IsCancellationRequested) break;
                    }

                    data = Buffer.Dequeue();

                    //  Wake up the sender if it was waiting for a dequeue
                    Monitor.Pulse(Buffer);
                }

                //------------------------------------
                //  Send data
                //------------------------------------

                try
                {
                    //  Send data
                    Task w = Stream.WriteAsync(data, 0, data.Length);

                    //  I really need the result now. Don't want to send packets out of order.
                    w.Wait(AbortTransfer.Token);
                    w.Dispose();

                    //  Update written count
                    written += get;
                    Progress.Completion = written;
                }
                catch (Exception e)
                {
                    lock (Buffer)
                    {
                        //  I have to notify the Loader that I couldn't write!
                        //  Read the considerations in the Loader.

                        Monitor.Pulse(Buffer);

                        //  Cancel the loader.
                        AbortTransfer.Cancel();

                        if(e is SocketException) Progress.CurrentActivity = "Socket has been closed";
                        else
                        {
                            if (e is OperationCanceledException) Progress.CurrentActivity = "Operation cancelled";
                            else Progress.CurrentActivity = "The other peer cancelled the operation";
                        }
                        
                        Progress.IsError = true;
                        StopSending = true;
                    }
                }
            }

            //  Send Digest
            if (written == FileSize && !AbortTransfer.Token.IsCancellationRequested)
            {
                lock (this)
                {
                    if (AbortTransfer.Token.IsCancellationRequested) return;
                    if (Buffer.Count == 0) Monitor.Wait(this);
                    if (AbortTransfer.Token.IsCancellationRequested) return;

                    byte[] Digest = Buffer.Dequeue();

                    using (Task t = Stream.WriteAsync(Digest, 0, 32))
                    {
                        t.Wait(AbortTransfer.Token);
                    }
                }
            }

        }

        /// <summary>
        /// Send the hello
        /// </summary>
        /// <returns></returns>
        private Response SendHello()
        {
            //------------------------------------
            //  Init
            //------------------------------------

            long size = FileSize;
            string extension = FileExtension,
                    name = FileName;
            byte[] message = Encoding.UTF8.GetBytes("HELLO");

            //  UPDATE: Since we are using unicode, we are going to send filename and extension in TLV format.

            //------------------------------------
            //  Send "Hello"
            //------------------------------------    

            if (Stream.CanWrite)
            {
                try
                {
                    //  Hello, network-mate! ...
                    Stream.Write(message, 0, message.Length);

                    //  ... I am a private user/
                    if (((Settings)Application.Current.Properties["Settings"]).Ghost) Stream.WriteByte(0);

                    //  /someone you already know...
                    else Stream.WriteByte(1);

                    //  ... I'd like to send you a file named [blah blah],...
                    message = Encoding.Unicode.GetBytes(name);
                    Stream.Write(BitConverter.GetBytes(message.Length), 0, 4);
                    Stream.Write(message, 0, message.Length);
                    Debug.WriteLine("Sent {0}", name);

                    //  ... it has extension [.ext], ...
                    message = Encoding.Unicode.GetBytes(extension);
                    Stream.Write(BitConverter.GetBytes(message.Length), 0, 4);
                    Stream.Write(message, 0, message.Length);
                    Debug.WriteLine("Sent {0}", extension);

                    //  ... it is [n-bytes] long, ...
                    //  NOTE: long.Length is always 64bits
                    message = BitConverter.GetBytes(size);
                    Stream.Write(message, 0, 8);
                    Debug.WriteLine("Sent {0} bytes (as size)", size);

                    //  ... and this is its thumbnail, ...
                    //  UPDATE: didn't implement it... in a real world application I would include:
                    //  Nuget: https://www.nuget.org/packages/WindowsAPICodePack-Shell

                    //  Do you accept?
                    //  I am expecting an "OK" message
                    byte[] reply = new byte[5];

                    //  ReadAsync does *NOT* really support cancellation, 
                    //  so in order to stop if someone cancelled it, I have to do it like this.
                    //  Here for more: https://github.com/dotnet/corefx/issues/15033
                    //  This will create a new CancellationTokenSource, that will cancel itself after 30 seconds
                    //  UPDATE: I decided to drop the timeout, and hang here because requirements don't require this.
                    /*using (CancellationTokenSource TimeOut = new CancellationTokenSource(30 * 1000))
                    {
                        //  It will also cancel itself if the MainCancellationToken says so...
                        using (CancellationTokenRegistration ctr = MainCancellationToken.Register(() => TimeOut.Cancel()))
                        {
                            Task<int> r = Stream.ReadAsync(reply, 0, reply.Length);
                            r.Wait(TimeOut.Token);
                        }
                    }*/

                    Task<int> r = Stream.ReadAsync(reply, 0, reply.Length);
                    r.Wait(MainCancellationToken);

                    //------------------------------------
                    //  Parse the reply
                    //------------------------------------ 

                    string response = Encoding.UTF8.GetString(reply).Trim('\0');
                    if (!response.Equals("OK"))
                    {
                        if (response.Equals("KO"))
                        {
                            Progress.CurrentActivity = "Request rejected";
                            Progress.IsError = true;
                            Debug.WriteLine("Rejected");
                            return Response.REJECTED;
                        }
                        Debug.WriteLine("Unknown!");
                        Progress.CurrentActivity = "Unknown answer";
                        Progress.IsError = true;
                        return Response.UNRECOGNIZED;
                    }

                    //  Ok
                    Debug.WriteLine("Accepted");
                    return Response.ACCEPTED;
                }
                /*catch (IOException)
                {
                    Progress.CurrentActivity = "Time out";
                    Progress.IsError = true;
                    return Response.REJECTED;
                }*/
                catch (OperationCanceledException)
                {
                    Progress.CurrentActivity = "Operation Cancelled";
                    Progress.IsError = true;
                    StopSending = true;
                    return Response.CANCELED;
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.Message);
                    Progress.CurrentActivity = "An error occurred";
                    Progress.IsError = true;
                    StopSending = true;
                    return Response.UKNOWN;
                }
            }

            //  Error occured
            StopSending = true;
            return Response.UKNOWN;
        }

        /// <summary>
        /// Wait for this task to finish. Used just when exiting application
        /// </summary>
        public void Wait()
        {
            if(Worker != null)
            {
                Worker.Wait();
            }            
        }

        /// <summary>
        /// Cancel current transfer
        /// </summary>
        public override void Cancel()
        {
            lock (Buffer)
            {
                //  Have to wake up who might be sleeping
                Monitor.PulseAll(Buffer);

                //  I put Cancel here, so that if the loader or sender are waiting for the lock,
                //  I am sure that they will get the cancellation when they get the lock
                //  NOTE: I may have been called *BEFORE* even starting the sender and the loader,
                //  that's why I am checking for null. No worries for Monitor.PulseAll, because it won't wake anyone.
                if (AbortTransfer != null) AbortTransfer.Cancel();
            }
        }
    }
}
