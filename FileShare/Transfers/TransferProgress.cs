using System;
using System.ComponentModel;
using System.Windows.Input;
using System.Timers;

namespace FileShare
{
    /// <summary>
    /// This class is the one who deals with the progress bar
    /// </summary>
    public class TransferProgress : INotifyPropertyChanged
    {
        /// <summary>
        /// The name of the user we're receiving from/sending to
        /// </summary>
        private string username = String.Empty;
        public string UserName
        {
            get { return username; }
            set
            {
                username = value;
                PropertyHasChanged(nameof(UserName));
            }
        }

        /// <summary>
        /// The profile picture
        /// </summary>
        private byte[] userpicture;
        public byte[] UserPicture
        {
            get { return userpicture; }
            set
            {
                if (value == null) return;

                //  Copy stuff, because if user vanishes from the collection we have no trace of him/her
                userpicture = new byte[value.Length];
                value.CopyTo(userpicture, 0);
                PropertyHasChanged(nameof(UserPicture));
            }
        }

        /// <summary>
        /// Sets/gets the arrow picture
        /// </summary>
        private string fromorto = String.Empty;
        public string FromOrTo
        {
            get
            {
                if (fromorto.Equals("from")) return @"/Assets/lower.png";
                if (fromorto.Equals("to")) return @"/Assets/greater.png";
                return String.Empty;
            }

            set
            {
                if (!value.Equals("from") && !value.Equals("to")) return;
                fromorto = value;
                PropertyHasChanged(nameof(FromOrTo));
            }
        }

        /// <summary>
        /// What the thread is currently doing
        /// </summary>
        private string currentactivity = String.Empty;
        public string CurrentActivity
        {
            get { return currentactivity; }
            set
            {
                currentactivity = value;
                PropertyHasChanged(nameof(CurrentActivity));
            }
        }

        /// <summary>
        /// Red if error, green if success
        /// </summary>
        private string textcolor = String.Empty;
        public string TextColor
        {
            get { return textcolor; }
            set
            {
                textcolor = value;
                TextWeight = "Bold";
                PropertyHasChanged(nameof(TextColor));
            }
        }

        /// <summary>
        /// The font weight
        /// </summary>
        private string textweight = "Normal";
        public string TextWeight
        {
            get { return textweight; }
            set
            {
                textweight = value;
                PropertyHasChanged(nameof(TextWeight));
            }
        }

        /// <summary>
        /// True if an error occurred
        /// </summary>
        private bool iserror = false;
        public bool IsError
        {
            get { return iserror; }
            set
            {
                iserror = value;
               
                //  So an error did occur after all
                if(value)
                {
                    TextColor = "Red";
                    Visibility = "Collapsed";
                }               
                
                PropertyHasChanged(nameof(IsError));
            }
        }

        /// <summary>
        /// Sets the visibility of some items
        /// </summary>
        private string visibility = String.Empty;
        public string Visibility
        {
            get { return visibility; }
            set
            {
                visibility = value;
                PropertyHasChanged(nameof(Visibility));
            }
        }

        /// <summary>
        /// Current progress
        /// </summary>
        private double completion;
        public double Completion
        {
            get { return completion; }
            set
            {
                // Get the lock, because the estimator might also use this
                lock (this)
                {
                    completion = value;
                }

                PropertyHasChanged(nameof(Completion));

                // Stop the estimator if it is done
                if(completion != 0 && completion == maximum)
                {
                    Estimator.Stop();
                    Visibility = "Collapsed";
                }
            }
        }

        /// <summary>
        /// The completion state on the last time we checked
        /// </summary>
        private double Previous = 0;

        /// <summary>
        /// Smoothing Factor to prevent the remaining time to fluctuate too much
        /// </summary>
        private double SmoothingFactor = 0.005;

        /// <summary>
        /// Last measured speed
        /// </summary>
        private double LastSpeed = 0;

        /// <summary>
        /// The average speed
        /// </summary>
        private double AverageSpeed = 0;

        /// <summary>
        /// Minimum value in the progress bar
        /// </summary>
        private long minimum = 0;
        public long Minimum
        {
            get { return minimum; }
            set
            {
                minimum = value;
                PropertyHasChanged(nameof(Minimum));
            }
        }

        private ElapsedEventHandler Delegator;

        /// <summary>
        /// The maximum
        /// </summary>
        private long maximum;
        public long Maximum
        {
            get { return maximum; }
            set
            {
                maximum = value;
                PropertyHasChanged(nameof(Maximum));

                //  Already started?
                if (Estimator != null) return;

                //------------------------------------
                //  Calculate recurrence
                //------------------------------------

                short recurrence = 1;

                //  UPDATE: I decided to delete this as it results fluctuate too much
                //  More than 100 MB?
                /*if (Maximum > 100 * 1024 * 1024) recurrence = 2;

                //  More than 500 MB?
                if (Maximum > 500 * 1024 * 1024) recurrence = 10;

                //  More than 1 GB?
                if (Maximum > 1024 * 1024 * 1024) recurrence = 30;*/

                Estimator = new Timer(recurrence * 1000)
                {
                    AutoReset = true
                };
                Estimator.Elapsed += Delegator;
                Estimator.Start();
            }
        }

        /// <summary>
        /// The file's name
        /// </summary>
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
        /// How much time is left
        /// </summary>
        private TimeSpan estimated;
        public double Estimated
        {
            get { return estimated.TotalSeconds; }
            set
            {
                if (IsError) return;
                estimated = TimeSpan.FromSeconds(value+1);

                //  Write how much has remained
                string _text = "~" + estimated.ToString("g");
                int i = _text.IndexOf('.');
                if (i > 0) EstimatedText = _text.Substring(0, i);
                else EstimatedText = _text;

                //  no need to raise events here
            }
        }

        /// <summary>
        /// The parent transfer, so we can cancel it
        /// </summary>
        public Transfer Parent { get; set; }

        /// <summary>
        /// The abort transfer command
        /// </summary>
        public Abort AbortTransfer { get; set; }

        /// <summary>
        /// The estimator, which estimates how much time is left (approximately)
        /// </summary>
        public Timer Estimator;

        /// <summary>
        /// How much time has remained
        /// </summary>
        private string estimatedText = String.Empty;
        public string EstimatedText
        {
            get { return estimatedText; }
            set
            {
                estimatedText = value;
                PropertyHasChanged(nameof(EstimatedText));
            }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public TransferProgress()
        {
            Delegator = new ElapsedEventHandler(CalculateEstimated);
            
            Completion = 0;
            AbortTransfer = new Abort(this);
            Visibility = "Visible";
        }
        
        /// <summary>
        /// The command to cancel the operation
        /// </summary>
        public class Abort : ICommand
        {
            /// <summary>
            /// The parent view model
            /// </summary>
            TransferProgress Parent;

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="parent">Parent view model</param>
            public Abort(TransferProgress parent)
            {
                Parent = parent;

                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }

            public bool CanExecute(object parameter)
            {
                return true;
            }

            public void Execute(object parameter)
            {
                if (Parent.Completion == Parent.Maximum || Parent.IsError) return;
                Parent.Cancel();
            }

            public event EventHandler CanExecuteChanged;
        }

        /// <summary>
        /// Cancel the operation
        /// </summary>
        public void Cancel()
        {
            Parent.Cancel();
            CurrentActivity = "Operation cancelled by user.";
            IsError = true;
        }

        /// <summary>
        /// Calculate how much time is remaining.
        /// </summary>
        /// <remarks>It is very very basic. 
        /// I found that this https://stackoverflow.com/a/3841706/ is a very good formula instead
        /// </remarks>
        private void CalculateEstimated(object sender, ElapsedEventArgs args)
        {
            // An error occurred?
            if(IsError)
            {
                Estimator.Stop();
                return;
            }

            //  Get current completion
            double C;
            lock (this)
            {
                C = completion;
            }

            // Not started or finished?
            if (C == 0) return;
            if (C == Maximum)
            {
                Estimator.Stop();
                Visibility = "Collapsed";
                return;
            }

            //  Actually calculate it
            double speed = (Previous - C) / 1;
            if (AverageSpeed == 0) AverageSpeed = LastSpeed;
            AverageSpeed = SmoothingFactor * LastSpeed + (1 - SmoothingFactor) * AverageSpeed;
            LastSpeed = speed;
            Estimated = ((Maximum - C) / AverageSpeed);
            Previous = C;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void PropertyHasChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
