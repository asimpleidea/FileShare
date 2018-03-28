using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows;
using System.Net.Sockets;
using System.Threading;
using System.IO;

namespace FileShare
{
    /// <summary>
    /// The Model. Doesn't have to do anything.
    /// We can say that the actual model should be the ConnectionsListener, but the would be wrong.
    /// </summary>
    class IncomingTransfer
    {
    }

    /// <summary>
    /// This class deals with the incoming connection, asking user if they want to accept the transfer
    /// </summary>
    public class IncomingTransferViewModel : INotifyPropertyChanged
    {
        /// <summary>
        /// The filename
        /// </summary>
        /// <remarks>Used only if the other peer is not private.</remarks>
        private string filename;
        public string FileName
        {
            get { return filename; }
            set
            {
                filename = value;
                PropertyHasChanged(nameof(FileName));
            }
        }

        /// <summary>
        /// Peer's profile picture
        /// </summary>
        /// <remarks>Used only if the other peer is not private.</remarks>
        private byte[] userprofilepicture;
        public byte[] UserProfilePicture
        {
            get { return userprofilepicture; }
            set
            {
                userprofilepicture = value;
                PropertyHasChanged(nameof(UserProfilePicture));
            }
        }

        /// <summary>
        /// Is he/she private?
        /// </summary>
        private bool privatesender = false;
        public bool PrivateSender
        {
            get { return privatesender; }
            set
            {
                privatesender = value;
                PropertyHasChanged(nameof(PrivateSender));
            }
        }

        /// <summary>
        /// The size of the file.
        /// </summary>
        /// <remarks>This is used only if is a file, not a folder.</remarks>
        private long _size;
        private long _Size
        {
            get { return _size; }
            set
            {
                _size = value;
                //  Size, not size. Because that's the one I want to see
                PropertyHasChanged(nameof(Size));
            }
        }
        public string Size
        {
            get
            {
                //  More than 1 GB?
                if(_Size > 1024*1024*1024) return String.Format("{0} GB", Math.Round((decimal)_Size / (1024*1024*1024), 2));
                if (_Size > 1024 * 1024) return String.Format("{0} MB", Math.Round((decimal)_Size / (1024 * 1024), 2));
                return String.Format("{0} KB", Math.Round((decimal)_Size /1024, 2));
            }
        }

        /// <summary>
        /// The path in which to place the download.
        /// </summary>
        private string selectedpath = String.Empty;
        public string SelectedPath
        {
            get { return selectedpath; }
            set
            {
                selectedpath = value;
                PropertyHasChanged(nameof(SelectedPath));
            }
        }

        /// <summary>
        /// The extension of the file.
        /// </summary>
        /// <remarks>Used only if is a file, not a folder.</remarks>
        private string extension;
        public string Extension
        {
            get { return "." + extension; }
            set
            {
                extension = value;
                PropertyHasChanged(nameof(Extension));
            }
        }

        /// <summary>
        /// The text
        /// </summary>
        /// <example>A private user wants to send you a file:</example>
        private string userrequesttext = String.Empty;
        public string UserRequestText
        {
            get { return userrequesttext; }
            set
            {
                userrequesttext = value;
                PropertyHasChanged(nameof(UserRequestText));
            }
        }

        /// <summary>
        /// Accepts the request
        /// </summary>
        public AcceptTransfer Accept { get; set; }

        /// <summary>
        /// Rejects the request
        /// </summary>
        public RejectTransfer Reject { get; set; }

        /// <summary>
        /// Opens dialog to choose the folder.
        /// </summary>
        public ChooseFolder SelectFolder { get; set; }

        /// <summary>
        /// The "pop up" window
        /// </summary>
        private ConnectionWindow TheWindow;

        /// <summary>
        /// Defines the Reply possibilities
        /// </summary>
        public enum Reply { UNANSWERED, ACCEPTED, REJECTED }

        /// <summary>
        /// The current status of the reply
        /// </summary>
        public Reply Status = Reply.UNANSWERED;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="w">The window</param>
        /// <param name="Client">The client</param>
        public IncomingTransferViewModel(ConnectionWindow w, TcpClient Client)
        {
            //------------------------------------
            //  Init
            //------------------------------------

            PrivateSender = false;
            TheWindow = w;
            SelectFolder = new ChooseFolder(this);

            //------------------------------------
            //  Add the transfer but don't start it yet
            //------------------------------------

            List<Task> Transfers = (List<Task>)Application.Current.Properties["Transfers"];
            Transfers.Add(Task.Run(() =>
            {
                Receive Receiver = new Receive(Client);
                if (!Receiver.ParseHello())
                {
                    //  Not a valid request? then just close the window (NOTE: it is still hidden at this point)
                    TheWindow.Dispatcher.InvokeAsync(() =>
                    {
                        TheWindow.Close();
                    });
                    return;
                }
                else
                {
                    //  Open the window to ask user to accept or reject
                    TheWindow.Dispatcher.InvokeAsync(() =>
                    {
                        TheWindow.InitializeComponent();
                        TheWindow.DataContext = this;
                        TheWindow.Topmost = true;
                        TheWindow.Show();
                        TheWindow.Activate();
                        TheWindow.Topmost = false;
                    });
                }

                //------------------------------------
                //  Render data
                //------------------------------------

                if (Receiver.SenderUser != null)
                {
                    UserRequestText = Receiver.SenderUser.Name + " wants to send you";
                    UserProfilePicture = Receiver.SenderUser.PicBytes;
                }
                else
                {
                    UserRequestText = "A private user wants to send you";
                    PrivateSender = true;
                }

                if(!Receiver.IsFolder)
                {
                    FileName = Receiver.FileName + "." + Receiver.FileExtension;
                    UserRequestText += " a file:";
                    _Size = Receiver.FileSize;
                    Extension = Receiver.FileExtension;
                }
                else
                {
                    string[] _name = Receiver.FileName.Split('/');
                    FileName = _name[0];
                    UserRequestText += " a folder:";
                }

                //------------------------------------
                //  Wait for user's confirmation
                //------------------------------------

                lock (this)
                {
                    if(Status == Reply.UNANSWERED) Monitor.Wait(this);
                }

                //------------------------------------
                //  Deal with user's decision
                //------------------------------------

                if (!SelectedPath.Equals(String.Empty)) Receiver.FolderLocation = SelectedPath;

                if (Status == Reply.ACCEPTED) Receiver.Start();
                else
                {
                    Receiver.Reject(true);
                }
            }));

            Accept = new AcceptTransfer(this);
            Reject = new RejectTransfer(this);
        }

        /// <summary>
        /// Start the transfer
        /// </summary>
        public void StartTransfer()
        {
            //------------------------------------
            //  Wake up the transfer thread
            //------------------------------------

            lock (this)
            {
                Status = Reply.ACCEPTED;
                Monitor.Pulse(this);
            }

            //------------------------------------
            //  Re-activate the Transfers Window
            //------------------------------------

            //  NOTE: Apparently, t.Show() or/with t.Activate() do not re-activate it.
            TransfersWindow t = (TransfersWindow)Application.Current.Properties["TransfersWindow"];
            t.WindowState = WindowState.Normal;
            t.Topmost = true;
            t.Show();
            t.Activate();
            t.Topmost = false;

            //  Now you can actually close this window
            TheWindow.Close();
        }

        /// <summary>
        /// Cancel every operation.
        /// </summary>
        /// <param name="ClosingWindow">Tells if the window must be closed</param>
        public void Cancel(bool ClosingWindow = false)
        {
            //------------------------------------
            //  Wake up the thread
            //------------------------------------

            lock (this)
            {
                Status = Reply.REJECTED;
                Monitor.Pulse(this);
            }

            //  Basically, if you called cancel because you clicked the X button
            if(!ClosingWindow) TheWindow.Close();
        }

        /// <summary>
        /// Chooses the folder
        /// </summary>
        public class ChooseFolder : ICommand
        {
            /// <summary>
            /// The View Model
            /// </summary>
            IncomingTransferViewModel Parent;            

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="parent">The parent view model</param>
            public ChooseFolder(IncomingTransferViewModel parent)
            {
                Parent = parent;

                //  Raise the event just to shut up the compiler
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }

            /// <summary>
            /// Tells if the command can execute
            /// </summary>
            /// <param name="parameter">The parameter</param>
            /// <returns>True on success</returns>
            public bool CanExecute(object parameter)
            {
                //  This can always fire                
                return true;
            }

            /// <summary>
            /// Executes the command
            /// </summary>
            /// <param name="parameter">The parameter with which we have been called by the view</param>
            public void Execute(object parameter)
            {
                //-----------------------------------
                //  Folder selection
                //-----------------------------------

                //  FolderBrowserDialog is a very UGLY window
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dialog.Description = "Your file will be placed here";
                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && !String.IsNullOrWhiteSpace(dialog.SelectedPath))
                    {
                        try
                        {
                            DirectoryInfo d = new DirectoryInfo(dialog.SelectedPath);

                            //  Just return... you just selected it, how can it not exist?
                            if (!d.Exists) return;
                        }
                        catch (Exception)
                        {
                            return;
                        }

                        //  check the path
                        Parent.SelectedPath = dialog.SelectedPath;
                    }
                }
            }

            /// <summary>
            /// The event handler
            /// </summary>
            public event EventHandler CanExecuteChanged;
        }

        /// <summary>
        /// Accepts the transfer
        /// </summary>
        public class AcceptTransfer : ICommand
        {
            /// <summary>
            /// The View model
            /// </summary>
            IncomingTransferViewModel ViewModel;

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="vm">The view model</param>
            public AcceptTransfer(IncomingTransferViewModel vm)
            {
                ViewModel = vm;

                //  Just to shut up the compiler
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }

            /// <summary>
            /// Tells if the command can execute
            /// </summary>
            /// <param name="parameter">the parameter</param>
            /// <returns></returns>
            public bool CanExecute(object parameter)
            {
                //  This can only be clicked if you have selected at least one element
                return true;
            }

            /// <summary>
            /// Executes the command
            /// </summary>
            /// <param name="parameter">the parameter</param>
            public void Execute(object parameter)
            {
                ViewModel.StartTransfer();
            }

            /// <summary>
            /// The event handler
            /// </summary>
            public event EventHandler CanExecuteChanged;
        }

        /// <summary>
        /// Rejects the transfer
        /// </summary>
        public class RejectTransfer : ICommand
        {
            /// <summary>
            /// The view model
            /// </summary>
            IncomingTransferViewModel ViewModel;

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="vm">The view model</param>
            public RejectTransfer(IncomingTransferViewModel vm)
            {
                ViewModel = vm;

                //  Again... shut up
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }

            /// <summary>
            /// Tells if the command can execute
            /// </summary>
            /// <param name="parameter">the parameter</param>
            /// <returns>true if yes</returns>
            public bool CanExecute(object parameter)
            {
                //  This can only be clicked if you have selected at least one element
                return true;
            }

            /// <summary>
            /// Executes the command
            /// </summary>
            /// <param name="parameter"></param>
            public void Execute(object parameter)
            {
                ViewModel.Cancel();
            }

            /// <summary>
            /// the event handler
            /// </summary>
            public event EventHandler CanExecuteChanged;
        }

        /// <summary>
        /// The event handler when a property has changed
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Called whenever a property has changed
        /// </summary>
        /// <param name="propertyName">Who called it</param>
        public void PropertyHasChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
