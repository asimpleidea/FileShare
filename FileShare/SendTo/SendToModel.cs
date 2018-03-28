using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.ComponentModel;
using System.Windows.Input; //  For ICommand interface
using System.Threading.Tasks;
using System.Net;
using System.Windows;

namespace FileShare
{
    /// <summary>
    /// The model.
    /// I don't really have anything to do here.
    /// </summary>
    public class SendToModel
    {
        /// <summary>
        /// Constructor
        /// </summary>
        public SendToModel()
        {
        }
    }

    /// <summary>
    /// The view model.
    /// </summary>
    public class SendToViewModel : INotifyPropertyChanged
    {
        /// <summary>
        /// The content (the list of users)
        /// </summary>
        private ContentControl content;
        public ContentControl Content
        {
            get { return content; }
            set
            {
                content = value;
                PropertyHasChanged(nameof(Content));
            }
        }

        /// <summary>
        /// The button which triggers the sending command
        /// </summary>
        public SendButton Button { get; set; }

        /// <summary>
        /// The window 
        /// </summary>
        private SendToWindow SendWindow;

        /// <summary>
        /// The file
        /// </summary>
        private string ClickedPath;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="w">The window</param>
        /// <param name="path">The file</param>
        public SendToViewModel(SendToWindow w, string path)
        {
            Content = new ChooseRecipient();
            Button = new SendButton(this);
            SendWindow = w;
            ClickedPath = path;
        }

        /// <summary>
        /// Start sending the transfer request
        /// </summary>
        /// <param name="users">Array of selected users to send the file to</param>
        public void StartSending(User[] users)
        {
            //------------------------------------
            //  Add this to current transfers
            //------------------------------------

            List<Task> Transfers = (List<Task>)Application.Current.Properties["Transfers"];

            //  Send a request for each user selected
            foreach(User u in users)
            {
                Transfers.Add(Task.Run(() =>
                {
                    Send s = new Send(ClickedPath, u);
                    s.Start();
                }));
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
            
            //  Close current window
            SendWindow.Close();
        }

        /// <summary>
        /// The Send Command
        /// </summary>
        public class SendButton : ICommand
        {
            /// <summary>
            /// The View Model
            /// </summary>
            SendToViewModel ViewModel;

            /// <summary>
            /// The RecipientsList
            /// Finding this will take some work traversing all the tree (à la JQuery, through the DOM),
            ///  so I'm storing it here, doing it just once.
            /// </summary>
            ListBox RecipientsList;

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="vm">The view model</param>
            public SendButton(SendToViewModel vm)
            {
                ViewModel = vm;
                RecipientsList = (ListBox)ViewModel.Content.FindName("RecipientsList");

                //  Whenever a new user is selected from the list, check if the button can be clicked
                RecipientsList.SelectionChanged += delegate { CanExecuteChanged?.Invoke(this, EventArgs.Empty); };
            }

            /// <summary>
            /// Tells if the command can execute or not
            /// </summary>
            /// <param name="parameter">the parameter</param>
            /// <returns>true if yes</returns>
            public bool CanExecute(object parameter)
            {
                //  This can only be clicked if you have selected at least one element
                return (RecipientsList.SelectedItems.Count > 0);
            }

            /// <summary>
            /// Execute the command
            /// </summary>
            /// <param name="parameter">the parameter</param>
            public void Execute(object parameter)
            {
                User[] selectedusers = new User[RecipientsList.SelectedItems.Count];

                int i = 0;
                foreach (User u in RecipientsList.SelectedItems) selectedusers[i++] = u;
             
                ViewModel.StartSending(selectedusers);
            }

            /// <summary>
            /// The event handler
            /// </summary>
            public event EventHandler CanExecuteChanged;
        }

        /// <summary>
        /// Will handle all code which subscribes to this (in this case, only the view)
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Called whenever a property has changed
        /// </summary>
        /// <param name="propertyName">Who called it</param>
        void PropertyHasChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
