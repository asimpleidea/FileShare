using System;
using System.Threading;
using System.Windows;
using System.ComponentModel;

namespace FileShare
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        //  NOTE: In order not to overcomplicate things, this class will not respect the MVVM pattern precisely. Sorry.
        //  Other classes will.
        MainWindowViewModel ViewModel;

        //  Did you press the Exit button? (not the X one)
        private bool ExitPressed = false;

        /// <summary>
        /// The Constructor
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            ViewModel = new MainWindowViewModel();

            //  Tell the View that data will be provided by the View Model (for this class there's actually no Model)
            DataContext = ViewModel;
        }

        /// <summary>
        /// Show the Main Window
        /// </summary>
        public void ShowMainWindow()
        {
            //------------------------------------
            //  Show 
            //------------------------------------

            //  Get the mouse position and show the window slightly above it (it is on the taskbar)
            //  NOTE: the very first time the window size is not actually calculated
            System.Drawing.Point point = System.Windows.Forms.Control.MousePosition;
            this.Left = point.X - 80*4/2 - 20;
            this.Top = point.Y - 180;

            this.Topmost = true;

            //  If it is already visible, just bring it on top 
            //  (this actually never happens because of the OnDeactivated event, but you never know...)
            if (this.IsVisible)
            {
                if (this.WindowState == WindowState.Minimized)
                {
                    this.WindowState = WindowState.Normal;
                }
            }
            else
            {
                this.Show();
            }

            this.Activate();
            this.Topmost = false;
        }

        /// <summary>
        /// Open the configurations window
        /// </summary>
        /// <param name="sender">The sender</param>
        /// <param name="e">The event arguments</param>
        private void OpenConfigurations(object sender, RoutedEventArgs e)
        {
            //  Open the configurations model and show it.
            Configurations c = new Configurations()
            {
                Topmost = true
            };
            c.Show();
            c.Topmost = false;
        }

        /// <summary>
        /// Application requirements say that the app must stay on the taskbar, so I am going to prevent the default behavior.
        /// </summary>
        /// <param name="sender">The sender</param>
        /// <param name="e">If you want to cancel the event</param>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //  Has Close() been called because of the X button?
            if (!ExitPressed)
            {
                //  If so, don't close the window (it will close the whole app!), just hide it
                e.Cancel = true;
                this.Hide();
                return;
            }

            //  If I am already exiting (= I am waiting for other windows and task to exit before exiting myself)
            //  then don't proceed...
            if ((bool)Application.Current.Properties["Exiting"]) return;

            //  Ask for confirmation
            MessageBoxResult result = MessageBox.Show("By closing the program all running transfers will be stopped.", "Close Anyway", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.No);
            if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            //------------------------------------
            //  Ok, we're actually exiting
            //------------------------------------

            Application.Current.Properties["Exiting"] = true;

            //  Cancel the other tasks. 
            //  By doing it this way they will gracefully end deleting/disposing every resource, without doing .Abort().
            ((CancellationTokenSource)Application.Current.Properties["MainCancellationSource"]).Cancel();

            //  Prevent the event (just for now)
            e.Cancel = true;

            //  I MUST exit ONLY when all running transfers have been stopped! ...
            ((Window)Application.Current.Properties["TransfersWindow"]).Closed += delegate
            {
                e.Cancel = false;
                return;
            };

            //  ... so close all other windows (you might have a SendTo window open...) and then...
            foreach (Window w in Application.Current.Windows)
            {
                if (w is SendToWindow || w is ConnectionWindow) w.Close();
            }

            //  ... and then close the TransfersWindow, when it closed it will fire the event above
            ((Window)Application.Current.Properties["TransfersWindow"]).Close();
        }

        /// <summary>
        /// Change application current status
        /// </summary>
        /// <example>If you're Public, you will be Private</example>
        /// <param name="sender">The sender</param>
        /// <param name="e">The event arguments</param>
        private void ChangeStatus(object sender, RoutedEventArgs e)
        {
            ViewModel.ChangeStatus();
        }

        /// <summary>
        /// Open the transfers window
        /// </summary>
        /// <param name="sender">The sender object</param>
        /// <param name="e">The event arguments</param>
        private void OpenTransfersWindow(object sender, RoutedEventArgs e)
        {
            ((Window)Application.Current.Properties["TransfersWindow"]).Show();
            ((Window)Application.Current.Properties["TransfersWindow"]).Activate();
        }

        /// <summary>
        /// Put me on taskbar if I am not on top
        /// </summary>
        /// <param name="sender">The sender</param>
        /// <param name="e">The event arguments</param>
        private void OnDeactivated(object sender, EventArgs e)
        {
            this.Hide();
        }

        /// <summary>
        /// Actually EXIT
        /// </summary>
        /// <param name="sender">The sender</param>
        /// <param name="e">The event arguments</param>
        private void OnExitPressed(object sender, RoutedEventArgs e)
        {
            ExitPressed = true;

            //  This will fire the OnClosing event, which will tell other tasks that they need to exit
            //  before exiting here
            this.Close();
        }
    }

    /// <summary>
    /// The MainWindow View Model
    /// This will provide data that will be shown by the View
    /// </summary>
    /// <remarks>Using INotifyPropertyChanged, so that changes are propagated to the view (it will subscribe to it)</remarks>
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        /// <summary>
        /// The Status Text
        /// </summary>
        /// <example>Go Public</example>
        private string statustext = String.Empty;
        public string StatusText
        {
            get { return statustext; }
            set
            {
                statustext = value;
                PropertyHasChanged(nameof(StatusText));
            }
        }

        /// <summary>
        /// The image of the status
        /// </summary>
        private string statusimage;
        public string StatusImage
        {
            get { return statusimage; }
            set
            {
                statusimage = value;
                PropertyHasChanged(nameof(StatusImage));
            }
        }

        /// <summary>
        /// The name of the Status Bar
        /// </summary>
        /// <example>FileShare (Private Mode)</example>
        private string statusbar = String.Empty;
        public string StatusBar
        {
            get { return statusbar; }
            set
            {
                statusbar = value;
                PropertyHasChanged(nameof(StatusBar));
            }
        }

        /// <summary>
        /// The constructor
        /// </summary>
        public MainWindowViewModel()
        {
            //  Just render the ChangeStatus button name
            ChangeStatusButton();
        }

        /// <summary>
        /// Toggles the change status button description
        /// </summary>
        /// <example>When you click "Go Private", it will display "Go Public"</example>
        private void ChangeStatusButton()
        {
            //  If you're currently private...
            //  NOTE: On the Settings class I described why I called this status as "Ghost"
            if (((Settings)Application.Current.Properties["Settings"]).Ghost == true)
            {
                StatusBar = "FileShare (Private Mode)";
                StatusText = "Go Public";
                StatusImage = "Assets/public.png";
            }
            else
            {
                StatusBar = "FileShare";
                StatusText = "Go Private";
                StatusImage = "Assets/private.png";
            }
        }

        /// <summary>
        /// Actually toggle your status
        /// </summary>
        public void ChangeStatus()
        {
            //  ChangeStatus (in class Settings) changes user's current status and returns the current one
            //  NOTE: when I first built the settings classed, I thought that it was useful to write it like that...
            ((Settings)Application.Current.Properties["Settings"]).ChangeStatus();

            //  Update the texts and images
            ChangeStatusButton();
        }

        /// <summary>
        /// The Property ChangedEventHandler
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// The PropertyChanged Event Handler
        /// </summary>
        /// <param name="propertyName">Who changed</param>
        void PropertyHasChanged(string propertyName = null)
        {
            //  C# 6 (or was it 5?) syntax, it is the same as writing if(PropertyChanged != null)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
