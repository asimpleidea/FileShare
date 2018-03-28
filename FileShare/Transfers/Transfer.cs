using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Net.Sockets;

namespace FileShare
{
    /// <summary>
    /// This class will provide data for both the Receiver and Sender
    /// </summary>
    public class Transfer : INotifyPropertyChanged
    {
        /// <summary>
        /// The Endpoint port
        /// </summary>
        protected int Port = 2000;

        /// <summary>
        /// The file name's (or the folder one's)
        /// </summary>
        private string filename = String.Empty;
        public string FileName
        {
            get { return filename; }
            set { filename = value; }
        }

        /// <summary>
        /// The file's size
        /// </summary>
        /// <remarks>Used only when dealing with a file.</remarks>
        private long filesize = 0;
        public long FileSize
        {
            get { return filesize; }
            set { filesize = value; }
        }

        /// <summary>
        /// The file's extension
        /// </summary>
        /// <remarks>Used only when dealing with a file, of course.</remarks>
        private string fileextension = String.Empty;
        public string FileExtension
        {
            get { return fileextension; }
            set { fileextension = value; }
        }

        /// <summary>
        /// The file's path
        /// </summary>
        private string filepath = String.Empty;
        public string FilePath
        {
            get { return filepath; }
            set { filepath = value; }
        }

        /// <summary>
        /// The buffer in which the loader will load data to be sent/written
        /// </summary>
        protected Queue<byte[]> Buffer = new Queue<byte[]>();

        /// <summary>
        /// The file's Digest
        /// </summary>
        protected byte[] Digest = new byte[32];

        /// <summary>
        /// A reference to the Main Cancellation Token
        /// </summary>
        /// <remarks>This cancellation token is used only when the app needs to exit</remarks>
        protected CancellationToken MainCancellationToken;

        /// <summary>
        /// A token source for when the transfer must stop
        /// </summary>
        /// <remarks>Used only to abort the transfer, not the whole app</remarks>
        protected CancellationTokenSource AbortTransfer;

        /// <summary>
        /// The actual worker. The one who does stuff.
        /// </summary>
        protected Task Worker;

        /// <summary>
        /// The socket stream
        /// </summary>
        protected NetworkStream Stream;

        /// <summary>
        /// Constructor
        /// </summary>
        public Transfer()
        {
            //  Get the main Cancellation Token
            MainCancellationToken = (CancellationToken)Application.Current.Properties["MainCancellationToken"];
        }

        /// <summary>
        /// Start transferring
        /// </summary>
        public virtual void Start() { }

        /// <summary>
        /// Cancel the transferring
        /// </summary>
        public virtual void Cancel() { }

        /// <summary>
        /// Executes instructions when a property has changed
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Called to raise the PropertyChanged event
        /// </summary>
        /// <param name="propertyName">Who called it</param>
        public void PropertyHasChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// The view model
    /// </summary>
    public class TransferViewModel : INotifyPropertyChanged
    {
        /// <summary>
        /// A collection of the current transfers
        /// </summary>
        public ObservableCollection<TransferProgress> Progresses { get; set; }

        /// <summary>
        /// The main cancellation token
        /// </summary>
        CancellationToken MainCancellationToken;

        /// <summary>
        /// The constructor
        /// </summary>
        public TransferViewModel()
        {
            MainCancellationToken = ((CancellationToken)Application.Current.Properties["MainCancellationToken"]);
            Progresses = (ObservableCollection<TransferProgress>)Application.Current.Properties["TransferProgresses"];
        }

        public void Close()
        {
            /*foreach(TransferProgress t in Progresses)
            {
                t.AbortParent().Wait();
            }*/
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void RaiseEvent(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
