using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Collections.ObjectModel;

namespace FileShare
{
    /// <summary>
    /// This clas will handle this User's keep alive packets and parse other users' ones.
    /// Every Recurrence seconds, a keep alive packet from the current user will be sent in Multicast.
    /// Every Recurrence seconds, the Users list will be checked to remove inactive users.
    /// Other keep alive packets will be parsed as soon as they are received to check if they're valid.
    /// If they are, they User will be added to the Users list.
    /// </summary>
    class MulticastManager
    {
        /// <summary>
        /// The port to send/receive packets to/from 
        /// </summary>
        int Port = 2017;

        /// <summary>
        /// The multicast address
        /// </summary>
        /// <remarks>The address is the FF02::1 all nodes address.</remarks>
        IPAddress MulticastAddress = IPAddress.Parse("FF02::1");

        /// <summary>
        /// The Client
        /// </summary>
        UdpClient Client = null;

        /// <summary>
        /// A reference to the users list
        /// See App.xaml.cs
        /// </summary>
        ObservableCollection<User> Users;

        /// <summary>
        /// The Cancellation Token
        /// </summary>
        CancellationToken Token;

        /// <summary>
        /// Send the keep alive packets every x seconds
        /// </summary>
        private int Recurrence = 10;

        /// <summary>
        /// Flag that will tell the sender that it needs to send.
        /// </summary>
        /// <remarks>
        /// I am using this flag because the sender might take too much time for whatever reason,
        /// so if Go is true, the sender will not wait for the next recurrence but go instantly
        /// </remarks>
        private bool Go = true;

        /// <summary>
        /// The constructor
        /// </summary>
        public MulticastManager()
        {
            //  Get the users
            Users = (ObservableCollection<User>)Application.Current.Properties["Users"];

            //  Get the token, so that I can be cancelled
            Token = ((CancellationTokenSource)Application.Current.Properties["MainCancellationSource"]).Token;

            //  Set up the Timer
            System.Timers.Timer timer = new System.Timers.Timer(Recurrence * 1000);

            //------------------------------------
            //  Start
            //------------------------------------

            try
            {
                Client = new UdpClient(Port, AddressFamily.InterNetworkV6);

                //  Join the multicast
                Client.JoinMulticastGroup(MulticastAddress);
                //Client.MulticastLoopback = false;

                //  Register the token: when the token is cancelled, clean up everything.
                CancellationTokenRegistration CTR = Token.Register(() => Clear());

                //------------------------------------
                //  Listener
                //------------------------------------

                Task listener = Task.Run(async () => await Listen());

                //------------------------------------
                //  Sender
                //------------------------------------

                Task sender = Task.Run(() => Send());

                timer.AutoReset = true;
                timer.Elapsed += delegate
                {
                    //  Every recurrence seconds, get the lock, set the flag as true (needs to send) and wake up the sender.
                    lock(this)
                    {
                        Go = true;
                        Monitor.Pulse(this);
                    }
                };
                timer.Start();

                //  Wait for them to finish
                sender.Wait();
                listener.Wait();
            }
            catch(Exception /*e*/)
            {
                //Debug.WriteLine("In exception because: {0}", e.Message);
            }
            finally
            {
                //Debug.WriteLine("In the finally block");
                timer.Stop();
            }
        }

        /// <summary>
        /// Clean up everything
        /// </summary>
        private void Clear()
        {
            Client.DropMulticastGroup(MulticastAddress);

            //  This will make the listener throw a socket exception, de-facto ending it.
            Client.Close();

            //  Important!
            //  The sender might be sleeping because it's waiting for the time to send everything!
            lock(this)
            {
                Monitor.Pulse(this);
            }
        }

        /// <summary>
        /// Listener
        /// </summary>
        /// <returns>A result from the task</returns>
        /// <remarks>
        /// Did it like this so that I can take advantage of the ReceiveAsync return type: 
        /// there's the buffer and the remote endpoint, so I know who I received this from!
        /// </remarks>
        private async Task Listen()
        { 
            //------------------------------------
            //  Start to listen
            //------------------------------------  

            List<Task> tasks = new List<Task>();

            while (!Token.IsCancellationRequested)
            {
                try
                {
                    //  Receive packets
                    UdpReceiveResult receive = await Client.ReceiveAsync();
                    byte[] buffer = receive.Buffer;

                    Debug.WriteLine("Received from " + receive.RemoteEndPoint.Address.ToString());

                    //  Ignore the datagram if it is in an unrecognized packet format
                    if (Convert.ToChar(buffer[0]) == 'K')
                    {
                        //  Fetch this user's data while I get the next datagram.
                        //  I add it to the list so that I can wait for it before exiting.
                        tasks.Add(Task.Run(() =>
                        {
                            FetchUserData(buffer, receive.RemoteEndPoint.Address);
                        }));
                    }
                }
                catch (Exception /*e*/)
                {
                    //Debug.WriteLine("Listener: terminated with exception {0}", e.Message);
                }
            }

            //  Normally this shouldn't wait too long because FetchUserData doesn't have much to do
            if (tasks.Count > 0) Task.WaitAll(tasks.ToArray());
            //Debug.WriteLine("Listener: Waited for all my children to end. Now I am ending too.");
        }

        /// <summary>
        /// Sender
        /// </summary>
        private void Send()
        {
            //------------------------------------
            //  Init
            //------------------------------------

            while (!Token.IsCancellationRequested)
            {
                lock(this)
                {
                    /**
                     * I *NEED* to wrap it in a if(Token.IsCancellationRequested) because:
                     * 1) I won't even enter the Monitor.Wait, wasting other time to wake this up, risking it won't even wake up
                     * 2) I need to check if sender has been woken up because an error occurred, needs to exit, or it really needs to work.
                     **/
                    if (Token.IsCancellationRequested) return;
                    if(!Go) Monitor.Wait(this);
                    if (Token.IsCancellationRequested) return;

                    //  Set this as false, so that at next occurrence I will sleep instead of continuosly sending packets.
                    Go = false;
                }

                //------------------------------------
                //  Send the packet
                //------------------------------------

                try
                {
                    /**
                     * NOTE: On my first implementation, I used to send Keep Alive packets even if user was private.
                     * On that implementation, private users just weren't listed as possible receivers, and even
                     * if you tried to send to them, they acted like they didn't exist (they shut down the connection
                     * without sending a reject message).
                     * 
                     * Later, I decided to not send Keep Alive packets for private users at all, because I thought
                     * that my first implementation broke the software requirements ("Non annunciano la propria presenza").
                     * I think this way is way more dangerous, because it is even less authenticated.
                     * But well... this is just a university assignment, after all.
                     **/

                    Debug.WriteLine("Going to Send the ka packet");

                    //  So yeah... send the packet only if I'm not private
                    if(!((Settings)Application.Current.Properties["Settings"]).Ghost)
                    {
                        Debug.WriteLine("Sending the packet...");
                        IPEndPoint dest = new IPEndPoint(MulticastAddress, Port);

                        //  Send the packet, precalculated by the Settings model. See it for more info.
                        byte[] packet = ((Settings)Application.Current.Properties["Settings"]).KeepAlivePacket;
                        Client.Send(packet, packet.Length, dest);
                    }

                    //------------------------------------
                    //  Remove inactive people
                    //------------------------------------

                    //  The users list is really inside the Application.Current.Properties Dictonary, which is Thread Safe,
                    //  (it is written in the documents). But this reference is not. So, do I really need to lock? 
                    lock (Users)
                    {
                        int count = Users.Count;
                        List<int> IndexesToRemove = new List<int>();

                        //  First, get users to remove
                        for(int i = 0; i < count; i++)
                        {
                            //  If you missed once or more, you go out  
                            if ((DateTime.Now - Users[i].Time).TotalSeconds > Recurrence * 2)
                            {
                                IndexesToRemove.Add(i);
                            }
                        }

                        //  Then, remove the ones you found.
                        //  I have to do it like this, otherwise I might be removing something while still in loop,
                        //  potentially running in a OutOfBands Exception.
                        //  NOTE: one way to prevent it is to update count when removing the element and doing i--,
                        //  so that it will check again the new i-th element. But this way is clearer to read
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            foreach (int i in IndexesToRemove)
                            {
                                Users.RemoveAt(i);
                            }
                        });
                        
                    }
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.Message);
                    //  Just don't do anything...
                    return;
                }
            }
        }

        /// <summary>
        /// This fetches user's data, while the sender focusses on next datagram.
        /// For a definition on the packet data, see the Settings model.
        /// </summary>
        /// <param name="buffer">The packet's content</param>
        /// <param name="ip">The user's IP</param>
        private void FetchUserData(byte[] buffer, IPAddress ip)
        {
            //------------------------------------
            //  Init
            //------------------------------------

            //  Temporary variables
            string username = String.Empty;
            byte[]  pictureMD5 = null, 
                    picture = null;

            //------------------------------------
            //  Get the username
            //------------------------------------  

            using (MemoryStream stream = new MemoryStream(buffer))
            {
                //  Because at position 0 there's the code
                stream.Position = 1;

                //  Get username length
                byte[] data = new byte[2];
                stream.Read(data, 0, 2);
                short length = BitConverter.ToInt16(data, 0);

                //  Get username
                data = new byte[length];
                stream.Read(data, 0, length);
                username = Encoding.Unicode.GetString(data).Trim('\0');

                Debug.WriteLine("User {0}, {1}", username, ip);

                //------------------------------------
                //  Put on List
                //------------------------------------  

                if (Token.IsCancellationRequested) return;

                lock (Users)
                {
                    if (stream.Position < stream.Length)
                    {
                        //  MD5 is always 16 bytes long
                        pictureMD5 = new byte[16];
                        
                        //  Get picture MD5 (to check if I need to update it
                        stream.Read(pictureMD5, 0, 16);
                    }

                    //  Find the user to update
                    bool found = false;
                    foreach(User u in Users)
                    {
                        if(ip.Equals(u.IP))
                        {
                            found = true;
                            u.Time = DateTime.Now;
                            u.Name = username;

                            //  Picture needs update?
                            if(pictureMD5 != null && u.PictureNeedsUpdate(pictureMD5))
                            {
                                //  Get picture bitmap
                                picture = new byte[(int)(stream.Length - stream.Position)];
                                stream.Read(picture, 0, (int)(stream.Length - stream.Position));
                                u.PicBytes = picture;
                            }

                            return;
                        }
                    }

                    //  User did not exist? Then create it
                    if (!found)
                    {
                        if (pictureMD5 != null)
                        {
                            //  Get picture bitmap
                            picture = new byte[(int)(stream.Length - stream.Position)];
                            stream.Read(picture, 0, (int)(stream.Length - stream.Position));
                        }

                        //  Tell the main application Dispatcher to add the user.
                        //  Using the Dispatcher because WPF windows are Single Threaded Apartments,
                        //  so they don't allow code inside their thread to be executed from another thread
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            Users.Add(new User()
                            {
                                Name = username,
                                IP = ip,
                                PictureMD5 = pictureMD5,
                                PicBytes = picture
                            });
                        });                        
                    }                    
                }
            }
        }
    }
}
