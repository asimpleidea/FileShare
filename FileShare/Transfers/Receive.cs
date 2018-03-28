using System;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.IO;
using System.Windows;
using System.Diagnostics;
using System.Collections.ObjectModel;

namespace FileShare
{
    /// <summary>
    /// This class will actually receive a file/folder.
    /// It will be done following a Producer-Consumer pattern: a task will load bytes from the socket,
    /// and another one will write data to disk. All asynchronously
    /// </summary>
    public class Receive : Transfer
    {
        /// <summary>
        /// The TCP client
        /// </summary>
        TcpClient Client;

        /// <summary>
        /// The loader which reads bytes from the socket
        /// </summary>
        Task Loader;

        /// <summary>
        /// The writer that writes data to disk
        /// </summary>
        Task Writer;

        /// <summary>
        /// The progress shown in the transfers window
        /// </summary>
        TransferProgress Progress = null;

        /// <summary>
        /// Am I receiving a folder?
        /// </summary>
        public bool IsFolder { get; private set; } = false;

        /// <summary>
        /// The user who's sending me
        /// </summary>
        public User SenderUser { get; private set; } = null;       
        
        /// <summary>
        /// The folder in which I am writing data
        /// </summary>
        public string FolderLocation { get; set; }

        /// <summary>
        /// Flag that will signal when no other file must be received by the other peer
        /// </summary>
        private bool StopReceiving = true;

        /// <summary>
        /// The constructor
        /// </summary>
        /// <param name="client">The client, gotten from the ConnectionsListener class</param>
        public Receive(TcpClient client) : base()
        {
            Client = client;
            Stream = Client.GetStream();
            Stream.Flush();
            FolderLocation = ((Settings)Application.Current.Properties["Settings"]).DefaultSavePath;
        }

        /// <summary>
        /// Start receiving the file
        /// </summary>
        public override void Start()
        {
            //  Start the worker
            Worker = Task.Run(() =>
            {
                /**
                 * lock(current_downloaders)
                 * {
                 *      //  i == 0 because there's a while for tasks that send directories
                 *      if(i == 0) enqueue(my_thread_id);
                 *      while(current_downloaders >= 3 || queuers.peek() != me)
                 *      {
                 *          Monitor.Wait(current_downloaders);
                 *      }
                 *      dequeue(my_thread_id);
                 *      ++current_downloaders;
                 * }
                 */

                Accept();

                //  Set up the cancellation token
                AbortTransfer = new CancellationTokenSource();

                while (!StopReceiving)
                {
                    //  Don't even start transferring if the application has to exit
                    //  NOTE: I have to put it here so that I don't even start checking & creating the file.
                    //  In the Send class I don't do this because it's not necessary.
                    if (MainCancellationToken.IsCancellationRequested) return;

                    Work();
                }

                Stream.Close();
                Stream.Dispose();
            });

            //  Wait for it to finish, otherwise we loose the owner task
            Worker.Wait();
            Worker.Dispose();
            Worker = null;
        }

        /// <summary>
        /// Do stuff
        /// </summary>
        private void Work()
        {
            //  If the main token (the one thrown by the main application) requests cancellation,
            //  then cancel the trasfer too.
            using (CancellationTokenRegistration ctr = MainCancellationToken.Register(() => Cancel()))
            {
                //  Prepare File
                PrepareFile();

                //  Don't start if user cancelled or the two above had errors
                if (AbortTransfer.IsCancellationRequested || MainCancellationToken.IsCancellationRequested)
                {
                    Progress.CurrentActivity = "Operation cancelled";
                    Progress.IsError = true;
                    DestroyIncompleteFile();
                    return;
                }

                //------------------------------------
                //  Read from network
                //------------------------------------

                Loader = Task.Run(() => Load(), AbortTransfer.Token);

                //------------------------------------
                //  Write to file
                //------------------------------------

                Writer = Task.Run(() => Write(), AbortTransfer.Token);

                Progress.CurrentActivity = "Transferring file...";

                Writer.Wait();
                Loader.Wait();

                Writer.Dispose();
                Loader.Dispose();

                if (!Progress.IsError)
                {
                    Progress.CurrentActivity = "Transfer Completed";
                    Progress.TextColor = "Green";
                }
                else
                {
                    DestroyIncompleteFile();
                }
            }

            Buffer.Clear();

            //------------------------------------
            //  Parse the next request
            //------------------------------------

            if (!ParseHello())
            {
                //  Reject and get the next file
                Reject();
                return;
            }

            //  Accept the transfer request (if the parsed messages wasn't ENDOC)
            if (!StopReceiving) Accept();
        }

        /// <summary>
        /// Parse the connection request
        /// </summary>
        /// <returns>True if request is valid, false if not.</returns>
        /// <remarks>I should change this method, making it throw exceptions when something happens instead of
        /// return bool. Remember this.</remarks>
        public bool ParseHello()
        {
            //------------------------------------
            //  Init
            //------------------------------------

            int length = 0,
                i = 0;
            byte[] buffer;

            try
            {
                //  NOTE: I have no guarantees that I will read as much as I specified. 
                //  In the first implementation, I was just doing something like Stream.Read(buffer,0,n),
                //  ignoring the fact that I could read less than n, trusting the fact that I was reading small data.
                //  Now, I am doing it like this to prevent such cases in which I read less than how much specified
                Debug.WriteLine("------------------------");
                Stream.ReadTimeout = 5 * 1000;

                //------------------------------------
                //  The type of message
                //------------------------------------

                //  Get the message
                buffer = new byte[5];
                while (i < 5) i += Stream.Read(buffer, 0, 5 - i);

                Debug.WriteLine("Received {0}", Encoding.UTF8.GetString(buffer));

                //  Parse the message type
                string MessageType = Encoding.UTF8.GetString(buffer);

                //  ENDOC means that I don't want to send you anything else (= End the connection),
                //  so the return true is useless
                if (MessageType.Equals("ENDOC"))
                {
                    //  Did I receive a ENDOC as very first message?
                    if (StopReceiving) return false;
                    StopReceiving = true;
                    return true;

                    /**
                     * SECURITY IMPLICATIONS
                     * 
                     * If an attacker spoofs their IP addres and sends ENDOC as very first message, this will
                     * stop the communication even before starting it. This is because this is not an authenticated protocol.
                     * 
                     * POSSIBILE SOLUTIONS
                     * 
                     * The ones listed in the HELLO below
                     **/
                }

                //  If is not ENDOC and not even HELLO, then discard the packet.
                //  NOTE: the first time, when this return false, the communication will be instantly ended.
                //  But if you're sending a list of files, this will tell the sender to proceed with next file.
                if (!MessageType.Equals("HELLO"))
                {
                    return false;
                }

                int isprivate = Stream.ReadByte();
                if(isprivate == -1)
                {
                    StopReceiving = true;
                    return true;
                }

                //  User is not private, but do I know who this is?
                if(((byte)isprivate).Equals(1))
                {
                    IPAddress ip = ((IPEndPoint)Client.Client.RemoteEndPoint).Address;
                    bool found = false;
                    ObservableCollection<User> users = (ObservableCollection<User>)Application.Current.Properties["Users"];
                    foreach(User u in users)
                    {
                        Debug.WriteLine("IP from remote endpoint: {0}, IP to check {1}", ip, u.IP);
                        if (u.IP.Equals(ip))
                        {
                            found = true;
                            SenderUser = new User(u);
                            break;
                        }
                    }

                    //  Don't accept transfer if I don't know who this is.
                    //  But what about private users? I don't know who they are either!
                    //  Well, software's requirements say that private users don't communicate their existence,
                    //  but can still send, so...
                    if (found == false)
                    {
                        Progress.CurrentActivity = "User not found";
                        Progress.IsError = true;
                        StopReceiving = true;
                        return false;
                    }
                }
                else
                {
                    if (((byte)isprivate).Equals(0)) SenderUser = null;
                    else
                    {
                        Progress.CurrentActivity = "Invalid request";
                        Progress.IsError = true;

                        //  I don't understand your message...
                        StopReceiving = true;
                        return false;
                    }
                }     

                //------------------------------------
                //  The file name
                //------------------------------------

                //  Get its length first
                //  At first, I used the set a fixed length of 200 bytes for file name,
                //  but wasn't sure if it would play nice with UNICODE. So I made it TLV-Style
                buffer = new byte[4];
                i = 0;
                while (i < 4) i += Stream.Read(buffer, 0, 4 - i);
                length = BitConverter.ToInt32(buffer, 0);

                //  Name of files can be like: "file", "folder1/file", "folder1/folder2/file"
                //  So, it must be at least one byte long
                if (length < 2) return false;

                //  Now get the name
                buffer = new byte[length];
                i = 0;
                while (i < length) i += Stream.Read(buffer, 0, length - i);
                FileName = Encoding.Unicode.GetString(buffer).Trim('/').Trim('\0');
                if (FileName.Length - FileName.Replace("/", "").Length > 0) IsFolder = true;

                Debug.WriteLine("Filename: {0}", FileName);
                //  Was wondering if there was a better method to count occurrences in a string than just looping it c-style.
                //  So I found this:
                //  https://stackoverflow.com/questions/541954/how-would-you-count-occurrences-of-a-string-within-a-string

                //------------------------------------
                //  The file's extension
                //------------------------------------

                //  Get its length
                buffer = new byte[4];
                i = 0;
                while (i < 4) i += Stream.Read(buffer, 0, 4 - i);
                length = BitConverter.ToInt32(buffer, 0);

                //  Now get the actual extension
                buffer = new byte[length];
                i = 0;
                while (i < length) i += Stream.Read(buffer, 0, length - i);
                FileExtension = Encoding.Unicode.GetString(buffer).Trim('\0');

                Debug.WriteLine("Extension: {0}", FileExtension);

                //------------------------------------
                //  Get the file size
                //------------------------------------

                buffer = new byte[8];
                i = 0;
                while (i < 8) i += Stream.Read(buffer, 0, 8 - i);
                FileSize = BitConverter.ToInt64(buffer, 0);                

                //  Return true and don't stop receiving, meaning that the request is valid
                StopReceiving = false;

                Progress = new TransferProgress
                {
                    Maximum = FileSize,
                    FileName = FileName,
                    UserName = SenderUser != null ? SenderUser.Name : "Private User",
                    UserPicture = SenderUser?.PicBytes,
                    FromOrTo = "from",
                    CurrentActivity = "Parsing Request...",
                    Parent = this
                };

                //  Add new Progress Bar                    
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ((ObservableCollection<TransferProgress>)Application.Current.Properties["TransferProgresses"]).Insert(0, Progress);
                });
                return true;

            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Reject the transfer
        /// </summary>
        /// <param name="RejectAll">If I have to stop the thread</param>
        public void Reject(bool RejectAll = false)
        {
            try
            {
                byte[] response = Encoding.UTF8.GetBytes("KO");
                Stream.Write(response, 0, response.Length);
            }
            catch (Exception)
            {
                AbortTransfer.Cancel();
                /*if (RejectAll) */StopReceiving = true;
                return;
            }
        }

        /// <summary>
        /// Accept the transfer request
        /// </summary>
        public void Accept()
        {
            try
            {
                byte[] ok = Encoding.UTF8.GetBytes("OK");
                Stream.Write(ok, 0, ok.Length);
            }
            catch (Exception)
            {
                Progress.CurrentActivity = "Could not accept transfer request";
                Progress.IsError = true;
                //  The only reasonable thing that might go wrong here, is that the other peer closed the connection.
                Cancel();
            }
        }

        /// <summary>
        /// Cancel the task
        /// </summary>
        public override void Cancel()
        {
            if(AbortTransfer != null) AbortTransfer.Cancel();
            StopReceiving = true;
        }

        /// <summary>
        /// Prepare the file on disk
        /// </summary>
        /// <remarks>I decided to implement an approach similar to that of Chrome's: when a file already exists,
        /// the new file will have an (n) appended to its name.</remarks>
        /// <example>If I am going to receive Assignment.pdf and it already exists,
        /// then I'm going to save it as Assignment (1).pdf .</example>
        private void PrepareFile()
        {
            string ChosenDirectory = FolderLocation;

            //------------------------------------
            //  Look if I can write
            //------------------------------------    

            //  Check if folder exists
            if (!Directory.Exists(ChosenDirectory))
            {
                Progress.CurrentActivity = "Download directory not found!";
                Progress.IsError = true;
                Cancel();
                return;
            }

            //  Check if I have permissions to write there.
            try
            {
                //  Create a random file name
                string _filename = Path.Combine(ChosenDirectory, Path.GetRandomFileName());
                using (FileStream f = File.Create(_filename)
                )
                { }

                //  If I am here, it means that I could write the file => Directory is writable
                if (File.Exists(_filename)) File.Delete(_filename);
                Debug.WriteLine("{0} is writable because I could write {1}", ChosenDirectory, _filename);
            }
            catch (Exception)
            {
                //  I wasn't able to write the file, it means I am not able to write there.
                //  What I am really interested in, is UnauthorizedAccessException, but other
                //  exceptions are somewhat rare to happen as the file name is created specifically by Path.
                Progress.CurrentActivity = "Could not write to download directory!";
                Progress.IsError = true;
                Cancel();
                Debug.WriteLine("{0} is NOT writable", ChosenDirectory);
                return;
            }

            //  How many folders do I need to create?
            string[] paths = FileName.Split('/');

            //  If there's only 1 element in directories, it means that this is a file
            if (paths.Length > 1)
            {
                for (int i = 0; i < paths.Length - 1; ++i) ChosenDirectory = Path.Combine(ChosenDirectory, paths[i]);

                try
                {
                    Directory.CreateDirectory(ChosenDirectory);
                }
                catch (Exception)
                {
                    Progress.CurrentActivity = "Could not create directory!";
                    Progress.IsError = true;
                    Cancel();
                    return;
                }
            }

            //------------------------------------
            //  Check for file existence
            //------------------------------------ 

            string name = string.Empty,
                    iterate = string.Empty;
            bool exists = true,

            //  For the mutex
            InitiallyOwned = true;

            //  I want others to stop here! They might do exactly the same calculations as I am about to do,
            //  in exactly the same time, making this whole algorithm useless!
            //  So, the next thread that will do this with the same file will know that the file already exists.
            using (Mutex FileCreation = new Mutex(InitiallyOwned, "PDS_FileCreationMutex", out bool CreatedNew))
            {
                /**
                 * From the documentation:
                 *      "If name is not null and initiallyOwned is true, 
                 *       the calling thread owns the named mutex only if createdNew is true after the call"
                 */
                //  Sleep if I don't own the mutex
                if (!(InitiallyOwned && CreatedNew)) FileCreation.WaitOne();

                //  I doubt an Exception might be thrown here as I already checked for directory
                //  existence and writablity. But I am in a Mutex here, I *MUST* make sure that it gets released
                try
                {
                    //  i = 1 because if it exists at first attempt, next filename to try is "filename (1).ext"
                    //  not "filename (0).ext"
                    //  Since I already split paths, let's use it!
                    string filename = paths[paths.Length - 1];
                    for (int i = 1; exists; ++i)
                    {
                        //  On first iteration, this will test for "C:\My\Path\Filename.ext" existence.
                        //  Every time an occurrence is found, it will test "C:\My\Path\Filename (1).ext" and so on.
                        name = Path.Combine(ChosenDirectory, filename + iterate + "." + FileExtension);
                        Debug.WriteLine("Testing for {0} for existence", Path.GetFileName(name));

                        //  File does not exist, create it
                        if (!File.Exists(name))
                        {
                            //  This returns a FileStream, but I'm going to ignore it for the moment.
                            using (FileStream f = File.Create(name))
                            {
                                //  The following lines could stay outside of the using

                                FilePath = name;
                                FileName = filename + iterate;
                                Progress.FileName = FilePath;
                                Debug.WriteLine("File to be created is: {0}", FilePath);
                                exists = false;
                            }
                        }
                        else iterate = " (" + i + ")";
                    }
                }
                catch (Exception)
                {
                    Progress.CurrentActivity = "Could not create the file";
                    Progress.IsError = true;
                    Cancel();
                    return;

                }
                finally //  Important! No matter what happens, release the mutex!
                {
                    FileCreation.ReleaseMutex();
                }
            }
        }

        /// <summary>
        /// Load data from socket
        /// </summary>
        private void Load()
        {
            //------------------------------------
            //  Init
            //------------------------------------

            long read = 0;

            //------------------------------------
            //  Load data from Socket
            //------------------------------------

            while (read < FileSize && !AbortTransfer.Token.IsCancellationRequested)
            {
                //  How many bytes you should read
                //  Casted to int as the maximum will always be 4096, which is the defaults size of Read btw
                int get = (int)(FileSize - read < 4096 ? (FileSize - read) : 4096);
                byte[] _data = new byte[get],
                        data;
                int _read = 0;

                try
                {
                    //  Actually load data
                    //  It seems that the overload ReadAsync(byte[], int32, int32, CancellationToken) 
                    //  doesn't really support cancellation. So I made it with a Wait(CancellationToken)
                    Debug.WriteLine("-- Loader: Going to read.");
                    using (Task<int> r = Stream.ReadAsync(_data, 0, get))
                    {
                        r.Wait(AbortTransfer.Token);

                        //  Completed?
                        _read = r.Result;
                        if (_read == 0) throw new SocketException();

                        //  Create new array
                        data = new byte[_read];
                        Array.Copy(_data, data, _read);
                    }
                }
                catch (Exception e)
                {
                    //  An error occurred, notify the Writer that it must exit
                    lock (Buffer)
                    {
                        //  If you're here, it means that you were able to get the lock either
                        //  because the Writer is sending or because it's sleeping.
                        //  In the latter case I *MUST* tell it to wake up and exit
                        Monitor.Pulse(Buffer);

                        if(e is SocketException) Progress.CurrentActivity = "An error occurred while reading from network";
                        else
                        {
                            if (e is OperationCanceledException) Progress.CurrentActivity = "Operation Cancelled.";
                            else Progress.CurrentActivity = "Operation cancelled by user.";
                        }
                        Progress.IsError = true;

                        //  Set the cancellation token.
                        //  I *MUST* set the cancellation token *WHILE* still in lock,
                        //  so that i can cancel the operation while the Writer is still blocked,
                        //  ensuring that it will catch the cancellation.
                        Cancel();
                    }

                    Debug.WriteLine("Error Happened: {0}", e.Message);
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
                         * The first one is because user might have interrupted it OR the Writer had an exception.
                         * The second one is to check if I have been awoken because there is data OR I have to quit.
                         **/

                        if (!AbortTransfer.Token.IsCancellationRequested)
                        {
                            Debug.WriteLine("-- Loader: 5 elements are already there. Going to sleep... {0}", DateTime.Now);
                            Monitor.Wait(Buffer);
                        }

                        //  Did you wake me up because there's data or because we have to exit?
                        //  Doing break, so it gracefully exits the loop
                        if (AbortTransfer.Token.IsCancellationRequested) break;
                    }

                    //  Put data into buffer
                    Buffer.Enqueue(data);

                    //  Wake up the Writer if it was sleeping (which is probably so the first time)
                    Monitor.Pulse(Buffer);
                }

                //  Update how many data I have sent
                read += _read;
                Debug.WriteLine("-- Loader: Read {0} out of {1}, {2}", read, FileSize, DateTime.Now);
            }

            //  If you downloaded the whole file, go read the Digest!
            if (read == FileSize && !AbortTransfer.Token.IsCancellationRequested) LoadDigest();
        }

        /// <summary>
        /// Get the Digest from the socket
        /// </summary>
        private void LoadDigest()
        {
            //------------------------------------
            //  Get the Digest
            //------------------------------------

            int read = 0;
            byte[] digest = new byte[32];

            while (read < 32)
            {
                int _read = 0;
                try
                {
                    byte[] data = new byte[1];

                    //  Read byte per byte, keeping it simple
                    using (Task<int> r = Stream.ReadAsync(data, 0, 1))
                    {
                        r.Wait(AbortTransfer.Token);

                        //  Completed?
                        _read = r.Result;
                        if (_read == 0) throw new SocketException();
                        digest[read] = data[0];
                        ++read;
                    }
                }
                catch (Exception)
                {
                    lock (Buffer)
                    {
                        Monitor.Pulse(Buffer);
                        Cancel();
                    }

                    return;
                }
            }

            //  Put the Digest on buffer and notify writer that the digest is ready
            lock (Buffer)
            {
                Buffer.Enqueue(digest);
                Monitor.Pulse(Buffer);
            }
        }

        /// <summary>
        /// Writes data to disk
        /// </summary>
        private void Write()
        {
            //------------------------------------
            //  Init
            //------------------------------------

            long written = 0;

            //------------------------------------
            //  Get data from buffer
            //------------------------------------

            using (FileStream File = new FileStream(FilePath, FileMode.Create, FileAccess.ReadWrite, System.IO.FileShare.ReadWrite, 4096))
            {
                while (written < FileSize && !AbortTransfer.IsCancellationRequested)
                {
                    //  How much should I write
                    int get = (int)(FileSize - written < 4096 ? (FileSize - written) : 4096);
                    byte[] data;

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

                        //  Wake up the loader if it was waiting for a dequeue
                        Monitor.Pulse(Buffer);
                    }

                    //------------------------------------
                    //  Write to disk
                    //------------------------------------

                    try
                    {
                        //  Send data
                        Task w = File.WriteAsync(data, 0, data.Length);

                        //  I really need the result now. Can't write data out of order
                        w.Wait(AbortTransfer.Token);
                        w.Dispose();

                        //  Update written count
                        written += data.Length;
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
                            Cancel();

                            Progress.CurrentActivity = "An error occurred";
                            Progress.IsError = true;
                        }

                        Debug.WriteLine("Got an exception: {0}", e.Message);
                    }
                }

                //  Check Sha
                if (written == FileSize && !AbortTransfer.Token.IsCancellationRequested)
                {
                    Debug.WriteLine("Check SHA256");
                    if (!CheckSha(File)) Debug.WriteLine("SHA256 Does not correspond!");
                    else Debug.WriteLine("SHA256 match");
                }
            }
        }

        /// <summary>
        /// Check if file is good
        /// </summary>
        /// <param name="f">the filestream</param>
        /// <returns>true if ok</returns>
        private bool CheckSha(FileStream f)
        {
            Progress.CurrentActivity = "Checking SHA256...";

            using (SHA256 sha = SHA256Managed.Create())
            {
                byte[] ComputedDigest = new byte[32];
                f.Position = 0;

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
                    Compute.Wait(AbortTransfer.Token);
                    ComputedDigest = Compute.Result;
                    if (ComputedDigest == null)
                    {
                        Debug.WriteLine("error in sha256");
                        Progress.CurrentActivity = "An error occurred while computing SHA256";
                        Progress.IsError = true;
                        return false;
                    }
                }
                catch (Exception)
                {
                    Debug.WriteLine("In Exception");
                    Progress.CurrentActivity = "An error occurred while computing SHA256";
                    Progress.IsError = true;
                    return false;
                }

                lock (Buffer)
                {
                    //  Which means: if the loader hasn't put the digest on buffer yet
                    if (Buffer.Count == 0 && !AbortTransfer.Token.IsCancellationRequested) Monitor.Wait(Buffer);
                    if (AbortTransfer.Token.IsCancellationRequested) return false;

                    Digest = Buffer.Dequeue();
                }

                //  Digests are equal?
                if (ComputedDigest.Length != Digest.Length)
                {
                    Progress.CurrentActivity = "File is corrupted or invalid";
                    Progress.IsError = true;
                    return false;
                }
                //  Is writing it like this considered bad programming?
                int i = 0;
                for (i = 0; i < ComputedDigest.Length && ComputedDigest[i] == Digest[i]; ++i) ;
                if (i != Digest.Length)
                {
                    Progress.CurrentActivity = "File is corrupted or invalid";
                    Progress.IsError = true;
                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Destructor
        /// </summary>
        ~Receive()
        {
            Client.Close();
        }

        /// <summary>
        /// Wait for the worker to finish.
        /// Used just when closing the application.
        /// </summary>
        public void Wait()
        {
            //  When it's null, it means that it already ended
            if(Worker != null)
            {
                Worker.Wait();
            }            
        }

        /// <summary>
        /// Destroys the file an error happened OR user cancelled the operation
        /// </summary>
        private void DestroyIncompleteFile()
        {
            //  Don't go ahead if file doesn't even exist (e.g.: no permission to write on folder)
            if (!File.Exists(FilePath)) return;

            //  Actually delete the file
            try
            {
                File.Delete(FilePath);
            }
            catch(Exception e)
            {
                Debug.WriteLine("Could not delete file because: {0}", e.Message);
            }
        }
    }
}
