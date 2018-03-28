using System;
using System.Linq;
using System.ComponentModel; // For INotifyPropertyChanged interface
using System.Windows.Input; //  For ICommand interface
using System.Windows.Forms;
using System.IO;

namespace FileShare
{
    /// <summary>
    /// This class provides data for the Configuration.xaml
    /// </summary>
    /// <remarks>
    /// Implements INotifyPropertyChanged, so changes are recognized by the view by subscribing to the event
    /// </remarks>
    public class SettingsViewModel : INotifyPropertyChanged
    {
        /// <summary>
        /// The save command
        /// </summary>
        public Save SaveConfigurations { get; private set; }

        /// <summary>
        /// The Choose dialog command
        /// </summary>
        public ChooseFile Choose { get; private set; }

        /// <summary>
        /// True if form is valid and has no errors
        /// </summary>
        public bool CanSave = true;

        /// <summary>
        /// The user's name
        /// </summary>
        private string name;
        public string Name
        {
            get { return name; }
            set
            {
                if (value != name)
                {
                    name = value.Trim();
                    PropertyHasChanged(nameof(Name));
                }
            }
        }

        /// <summary>
        /// The error text in the username
        /// </summary>
        /// <example>
        /// Username length must be longer than 4 characters and shorter than 50
        /// </example>
        private string usernameerror;
        public string UserNameError
        {
            get { return usernameerror; }
            set
            {
                if (value != usernameerror)
                {
                    usernameerror = value.Trim();
                    PropertyHasChanged(nameof(UserNameError));
                }
            }
        }

        /// <summary>
        /// The error in folder
        /// </summary>
        /// <example>
        /// Folder does not exist
        /// </example>
        private string foldererror;
        public string FolderError
        {
            get { return foldererror; }
            set
            {
                if (value != foldererror)
                {
                    foldererror = value.Trim();
                    PropertyHasChanged(nameof(FolderError));
                }
            }
        }

        /// <summary>
        /// The error in the image
        /// </summary>
        /// <example>Image is too big</example>
        private string imageerror;
        public string ImageError
        {
            get { return imageerror; }
            set
            {
                imageerror = value;
                PropertyHasChanged(nameof(ImageError));
            }
        }

        /// <summary>
        /// The default path in which files will be saved if user does not provide a different folder
        /// </summary>
        private string path = String.Empty;
        public string Path
        {
            get { return path; }            
            set
            {
                if (path != value)
                {
                    path = value;
                    PropertyHasChanged(nameof(Path));
                }
            }
        }

        /// <summary>
        /// User's profile picture path
        /// </summary>
        private string picture = String.Empty;
        public string Picture
        {
            get { return picture; }
            set
            {
                picture = value;
                PropertyHasChanged(nameof(Picture));
            }
        }

        /// <summary>
        /// Automatically accept files flag
        /// </summary>
        private bool autoaccept = false;
        public bool AutoAccept
        {
            get { return autoaccept; }
            set { autoaccept = value; PropertyHasChanged(nameof(AutoAccept)); }
        }

        /// <summary>
        /// The result text
        /// </summary>
        /// <example>Settings saved!</example>
        private string resulttext;
        public string ResultText
        {
            get { return resulttext; }
            set
            {
                resulttext = value;
                PropertyHasChanged(nameof(ResultText));
            }
        }

        /// <summary>
        /// The result text color.
        /// Red for error, green for success
        /// </summary>
        private string resultcolor;
        public string ResultColor
        {
            get { return resultcolor; }
            set
            {
                resultcolor = value;
                PropertyHasChanged(nameof(ResultColor));
            }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public SettingsViewModel()
        {
            //  default color
            ResultColor = "Green";
            LoadValues();
            SaveConfigurations = new Save(this);
            Choose = new ChooseFile(this);
        }

        /// <summary>
        /// Load values from the settings class (which were loaded by the registry)
        /// </summary>
        private void LoadValues()
        {
            //-----------------------------------
            //  Load values from settings
            //-----------------------------------

            Settings s = (Settings)System.Windows.Application.Current.Properties["Settings"];
            Name = s.Name;
            Path = s.DefaultSavePath;
            Picture = s.PicturePath;
            AutoAccept = s.AutoAccept;
        }

        /// <summary>
        /// Update the settings. 
        /// </summary>
        /// <remarks>
        /// This is just a View Model, it just assists the View in providing data and decides how they have to be rendered. 
        /// The settings are actually saved by the Model class.
        /// </remarks>
        public void Update()
        {
            Settings s = (Settings)System.Windows.Application.Current.Properties["Settings"];
            if(!s.Update(Name, Path, Picture, AutoAccept))
            {
                //  Set Error message;
                //  UPDATE: no need to.
                return;
            }
        }

        /// <summary>
        /// The Save Class
        /// </summary>
        /// <remarks>
        /// Implements the ICommand interface, providing useful events and the Execute method
        /// </remarks>
        public class Save : ICommand
        {
            /// <summary>
            /// The view model
            /// </summary>
            SettingsViewModel Parent;

            /// <summary>
            /// The constructor
            /// </summary>
            /// <param name="parent">The View Model</param>
            public Save(SettingsViewModel parent)
            {
                Parent = parent;

                //  Whenever a property of SettingsViewModel is changed by me, raise the CanExecuteChanged event:
                //  which *suggests* that the result of CanExecute might have changed
                Parent.PropertyChanged += delegate { CanExecuteChanged?.Invoke(this, EventArgs.Empty); };
            }

            /// <summary>
            /// CanExecute
            /// </summary>
            /// <param name="parameter">The parameter provided by the view</param>
            /// <returns></returns>
            public bool CanExecute(object parameter)
            {
                //  This can always execute, the form will be checked on Execute
                return true;
            }

            /// <summary>
            /// Execute the command
            /// </summary>
            /// <param name="parameter">The Parameter provided by the view</param>
            public void Execute(object parameter)
            {
                //-----------------------------------
                //  Reset
                //-----------------------------------

                Parent.UserNameError = String.Empty;
                Parent.FolderError = String.Empty;
                Parent.ImageError = String.Empty;
                Parent.ResultText = String.Empty;
                Parent.CanSave = true;

                bool ValidName = false;

                //-----------------------------------
                //  Validate the input
                //-----------------------------------

                //  Only letter and spaces (e.g.: Marco Rossi)
                ValidName = Parent.Name.All((c) =>
                {
                    return (char.IsLetter(c) || char.IsWhiteSpace(c));
                });

                if (ValidName == false)
                {
                    Parent.CanSave = false;
                    Parent.UserNameError = "Username Not Valid";
                }

                //  Minimum of 4 characters and a maximum of 50
                //  Dumb numbers because this is just a university project, no thorough research has been made.
                if(Parent.Name.Length < 4 || Parent.Name.Length > 50)
                {
                    Parent.CanSave = false;
                    Parent.UserNameError = "Username must be longer than 4 characters and shorter than 50 characters.";
                }

                //  Check if user has an image. Usually it wouldn't matter but... I'm forcing you to have it anyway
                if(Parent.Picture.Length < 1)
                {
                    Parent.CanSave = false;
                    Parent.ImageError = "Please select an image.";
                }

                if(Parent.Path.Length < 1)
                {
                    Parent.CanSave = false;
                    Parent.FolderError = "Please select a valid directory.";
                }

                if (!Parent.CanSave)
                {
                    Parent.ResultText = "Could not save because of form errors.";
                    Parent.ResultColor = "Red";
                    return;
                }

                Parent.ResultText = "Settings saved!";
                Parent.ResultColor = "Green";
                Parent.Update();
            }

            /// <summary>
            /// The event handler
            /// </summary>
            public event EventHandler CanExecuteChanged;
        }

        /// <summary>
        /// This will open the Choose file/folder dialog, also checking some stuff
        /// </summary>
        public class ChooseFile : ICommand
        {
            /// <summary>
            /// The view model
            /// </summary>
            SettingsViewModel Parent;

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="parent">The View Model</param>
            public ChooseFile(SettingsViewModel parent)
            {
                Parent = parent;

                //  Everytime a property of Parent has changed, check if the button can actually execute
                Parent.PropertyChanged += delegate { CanExecuteChanged?.Invoke(this, EventArgs.Empty); };
            }

            /// <summary>
            /// CanExecute
            /// </summary>
            /// <param name="parameter">The parameter provided by the view</param>
            /// <returns></returns>
            public bool CanExecute(object parameter)
            {
                //  This can always fire
                return true;
            }

            /// <summary>
            /// Execute the command
            /// </summary>
            /// <param name="parameter">The Parameter provided by the view</param>
            public void Execute(object parameter)
            {
                //  Get the parameter (folder or file)
                string p = parameter.ToString();

                //-----------------------------------
                //  Folder selection
                //-----------------------------------

                if (p.Equals("folder"))
                {
                    //  FolderBrowserDialog is a very UGLY window
                    using (var dialog = new FolderBrowserDialog())
                    {
                        dialog.Description = "All downloads will be placed in this folder, unless you specify another folder upon transfer request.";
                        if (dialog.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                        {
                            try
                            {
                                DirectoryInfo d = new DirectoryInfo(dialog.SelectedPath);
                                if (!d.Exists) return;
                            }
                            catch(Exception)
                            {
                                Parent.CanSave = false;
                                Parent.FolderError = "Directory does not exist.";
                                return;
                            }                      
                            
                            Parent.Path = dialog.SelectedPath;
                        }
                    }

                    return;
                }

                //-----------------------------------
                //  Picture selection
                //-----------------------------------

                //  Using complete namespace as system.win32 has it too
                using (System.Windows.Forms.OpenFileDialog dialog = new System.Windows.Forms.OpenFileDialog())
                {
                    dialog.Title = "Select your profile picture.";
                    dialog.Filter = "Image Files(*.JPG; *.JPEG; *.PNG)| *.JPG; *.JPEG; *.PNG;";
                    dialog.CheckFileExists = true;
                    dialog.CheckPathExists = true;
                    dialog.DefaultExt = "jpg";
                    dialog.InitialDirectory = Directory.GetCurrentDirectory();
                    dialog.Multiselect = false;
                    dialog.ValidateNames = true;
                    if (dialog.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.FileName))
                    {
                        //  Pictures cannot be larger than 1 MB
                        FileInfo f = new FileInfo(dialog.FileName);
                        if (f.Length >= 1024 * 1024)
                        {
                            Parent.CanSave = false;
                            Parent.ImageError = "Images must be less than 1 MB.";
                            return;
                        }

                        Parent.Picture = dialog.FileName;
                    }
                }                    
            }

            /// <summary>
            /// The Event Handler
            /// </summary>
            public event EventHandler CanExecuteChanged;
        }

        /// <summary>
        /// The event handler. This will execute code when a property has changed
        /// I don't use it here, but the View is subscribed to this
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Called when a property has changed
        /// </summary>
        /// <param name="propertyName">Who called this</param>
        void PropertyHasChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
