using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using System.IO;
using System.Drawing;

namespace FileShare
{ 
    /// <summary>
    /// The settings Model
    /// This class acts as a sort of cache of settings
    /// </summary>
    /// <remarks>
    /// All properties here have a private set, so that *ONLY* this class can make changes.
    /// C# has a Settings class already, but I found it only when I was too deep in the project.
    /// Man, if I I only have found it earlier...
    /// Upon start, the application pre-calculates my keep alive packet, so that the Multicast Manager won't have
    /// to do that every single time is must send it.
    /// </remarks>
    class Settings
    {
        /// <summary>
        /// Logged user's current name
        /// </summary>
        private string name = String.Empty;
        public string Name
        {
            get { return name; }
            private set { name = value.Trim(); }
        }

        /// <summary>
        /// Logged user's default saving path
        /// </summary>
        private string defaultsavepath = String.Empty;
        public string DefaultSavePath
        {
            get { return defaultsavepath; }
            private set { defaultsavepath = value; }
        }

        /// <summary>
        /// The thumbnails path
        /// </summary>
        /// <remarks>
        /// This used to serve as the path in which other user's pictures would be stored.
        /// Later, I decide to store them as MemoryStream, to avoid the hassle of checking,
        /// deleting, saving and without wasting too much time in it.
        /// So, this is useless.
        /// </remarks>
        public string ThumbnailsPath { get; private set; }

        /// <summary>
        /// Logged user's picture
        /// </summary>
        private string picturepath = String.Empty;
        public string PicturePath
        {
            get { return picturepath; }
            private set
            {
                picturepath = value;

                //  Get a thumbnail not the actual picture!
                //  I can't send a 500KB image in a datagram man!
                GetThumbnail();
            }
        }

        /// <summary>
        /// This is the project directory. Used later for the registry and stuff.
        /// </summary>
        private string ProjectDirectory;

        /// <summary>
        /// The picture's byte stream.
        /// </summary>
        public byte[] PictureBitMap { get; private set; }

        /// <summary>
        /// The picture's MD5.
        /// I am storing the picture's MD5 to notify other users about logged user's current picture.
        /// </summary>
        /// <example>
        /// At some point, user A changes their picture: user B will check the MD5 contained in user A's keep alive
        /// and will know that user A changed their picture, storing and display user A's new picture.
        /// </example>
        /// <remarks>
        /// For files, I SHA256-ed the file and here I MD5-ed the picture, why?
        /// Because:
        /// 1) I am using UDP to send the picture: I want to occupy as little space as possible (64K MAX DATAGRAM SIZE)
        /// 2) *Very* small collision probability in this case (it will be checked only against this user's picture)
        /// 3) No dangers if a collision occurred (picture just won't change).
        /// </remarks>
        public byte[] PictureMD5 { get; private set; } = new byte[16];

        /// <summary>
        /// Transfer requests will be automatically accepted?
        /// </summary>
        public bool AutoAccept { get; private set; }

        /// <summary>
        /// Am I a private user?
        /// </summary>
        /// <remarks>
        /// In reality, you are not an actual private user: you are a GHOST.
        /// This protocol is very unsafe because it is *NOT* authenticated (it's just a university assignment).
        /// I could spoof my IP of an actual user one, and the receiver will notice no changes.
        /// But that can be fixed by making all users calculate a private RSA key and a public RSA key, propagating
        /// the public key with the keep alive. Then, on transfer request, I send a random non-repeatable number (challenge)
        /// encrypted with sender's public key, if they can get me back the number in plain format, then I know it's them.
        /// OR I could send my digital signature (SHA256 of all previous data, encrypted with my private key).
        /// But what about private users? You don't even have to spoof an IP! Recognizing a fake users would be hard.
        /// That's why, on first implementation, I used to send keep alive packets even if you were private,
        /// so that I know ALL users currently using the app, even the private ones (though I could not send to them).
        /// And so, this is the story of why I called it Ghost Mode here, and not Private Mode.
        /// </remarks>
        public bool Ghost { get; private set; }

        /// <summary>
        /// My Keep Alive Packet
        /// </summary>
        public byte[] KeepAlivePacket { get; private set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public Settings()
        {
            ProjectDirectory = Directory.GetParent(Directory.GetCurrentDirectory()).Parent.FullName;
            Load();
            CalculateKeepAlivePacket();
        }

        /// <summary>
        /// Load the settings from the registry
        /// </summary>
        private void Load()
        {
            //-----------------------------------
            //  Load values from registry
            //-----------------------------------

            using (RegistryKey r = Registry.CurrentUser.OpenSubKey("software", true))
            {
                //  Set the context menu
                CreateContextMenu(r);

                //  Check my application:
                //  Does it exist on registry?
                if (r.OpenSubKey("pds", true) == null)
                {
                    //  Then go set defaults
                    GetSetDefaults(r);

                    return;
                }

                //  Actually retrieve values
                using (RegistryKey app = r.OpenSubKey("pds"))
                {
                    Name = (string)app.GetValue("username");
                    DefaultSavePath = (string)app.GetValue("path");
                    AutoAccept = ((int)app.GetValue("autoaccept") == 1);
                    Ghost = ((int)app.GetValue("ghost") == 1);
                    ThumbnailsPath = (string)app.GetValue("thumbnails");

                    //  This has to be done as the last step, as it needs some previous data
                    PicturePath = (string)app.GetValue("userpic");
                }
            }
        }

        /// <summary>
        /// Change the status
        /// </summary>
        /// <returns>True if currently private user, false if public</returns>
        public bool ChangeStatus()
        {
            //  Revert its status
            Ghost = !Ghost;

            //  Update the value on the registry
            using (RegistryKey r = Registry.CurrentUser.OpenSubKey("software", true).OpenSubKey("pds", true))
            {
                r.SetValue("ghost", Ghost, RegistryValueKind.DWord);
            }

            //  Return the current value
            return Ghost;
        }

        /// <summary>
        /// Sets default values and then get them baco
        /// </summary>
        /// <param name="r">The registry key (current user)</param>
        private void GetSetDefaults(RegistryKey r)
        {
            //-----------------------------------
            //  Init
            //-----------------------------------

            string name = String.Empty,
                    path = String.Empty,
                    picture = String.Empty,
                    thumbnails = String.Empty;

            //-----------------------------------
            //  Get values from current user
            //-----------------------------------

            using (RegistryKey k = Registry.CurrentUser.OpenSubKey("Volatile Environment"))
            {
                name = (string)k.GetValue("username");
                path = (string)k.GetValue("localappdata");
                thumbnails = path;

                //  Create the default downloads directory
                try
                {
                    //  Create default downloads directory
                    Directory.CreateDirectory(path + @"\PDS\Downloads");
                    path += @"\PDS\Downloads";

                    //  Create default downloads directory
                    Directory.CreateDirectory(path + @"\PDS\Thumbnails");
                    thumbnails += @"\PDS\Thumbnails";
                }
                catch (Exception)
                {
                    //  Basically forcing the user to actually choose a folder
                    path = Directory.GetCurrentDirectory();
                    thumbnails = path;
                }

                //  Well... I couldn't find the default windows picture from the registry, so...
                picture = Path.Combine(ProjectDirectory, @"Assets\defaultpicture.jpg");

                //-----------------------------------
                //  Set my defaults
                //-----------------------------------

                using (RegistryKey app = r.CreateSubKey("pds"))
                {
                    app.SetValue("username", name, RegistryValueKind.String);
                    app.SetValue("path", path, RegistryValueKind.String);
                    app.SetValue("thumbnails", thumbnails, RegistryValueKind.String);
                    app.SetValue("userpic", picture, RegistryValueKind.String);
                    app.SetValue("autoaccept", 0, RegistryValueKind.DWord);
                    app.SetValue("ghost", 0, RegistryValueKind.DWord);
                }

                //-----------------------------------
                //  Update properties
                //-----------------------------------

                Name = name;
                DefaultSavePath = path;
                PicturePath = picture;
                AutoAccept = false;
                ThumbnailsPath = thumbnails;
                Ghost = false;
            }
        }

        /// <summary>
        /// Create the context menu (Send to...)
        /// </summary>
        /// <param name="r">The registry key</param>
        private void CreateContextMenu(RegistryKey r)
        {
            //-----------------------------------
            //  Create Context Menu
            //-----------------------------------

            try
            {
                //  Does shell exist?
                if (r.OpenSubKey(@"classes\*\shell") == null)
                {
                    r.CreateSubKey(@"classes\*\shell");
                }

                //  For files
                using (RegistryKey k = r.OpenSubKey(@"classes\*\shell", true))
                {
                    using (RegistryKey e = k.CreateSubKey("pds"))
                    {
                        //  This is the default
                        e.SetValue("", "Send To...");
                        e.SetValue("Icon", Path.Combine(ProjectDirectory, @"Assets\share.ico"));

                        //  Create the command
                        using (RegistryKey c = e.CreateSubKey("command"))
                        {
                            c.SetValue("", Path.Combine(ProjectDirectory, @"bin\Debug\") + "FileShare.exe \"%1\"");
                        }
                    }
                }

                //  Does shell exist?
                if (r.OpenSubKey(@"classes\Directory\shell") == null)
                {
                    r.CreateSubKey(@"classes\Directory\shell");
                }

                //  For directories
                using (RegistryKey k = r.OpenSubKey(@"classes\Directory\shell", true))
                {
                    using (RegistryKey e = k.CreateSubKey("pds"))
                    {
                        //  This is the default
                        e.SetValue("", "Send To...");
                        e.SetValue("Icon", Path.Combine(ProjectDirectory, @"Assets\share.ico"));

                        //  Create the command
                        using (RegistryKey c = e.CreateSubKey("command"))
                        {
                            c.SetValue("", Path.Combine(ProjectDirectory, @"bin\Debug\") + "FileShare.exe \"%1\"");
                        }
                    }
                }
            }
            catch (Exception)
            {
                //  Just don't add it
            }
        }

        /// <summary>
        /// Destroys Context Menu commands. Called upon exit
        /// </summary>
        private void DestroyContextMenu()
        {
            using (RegistryKey r = Registry.CurrentUser.OpenSubKey("software", true))
            {
                using (RegistryKey shell = r.OpenSubKey(@"classes\*\shell", true))
                {
                    shell.OpenSubKey("pds", true).DeleteSubKey("command", false);
                    shell.DeleteSubKey("pds", false);
                }

                using (RegistryKey shell = r.OpenSubKey(@"classes\Directory\shell", true))
                {
                    shell.OpenSubKey("pds", true).DeleteSubKey("command", false);
                    shell.DeleteSubKey("pds", false);
                }
            }
        }

        /// <summary>
        /// Precalculate the Keep Alive Packet,
        /// so that the Multicast Manager won't have to do it each time it has to send it
        /// </summary>
        private void CalculateKeepAlivePacket()
        {
            byte[] username = Encoding.Unicode.GetBytes(Name);

            //------------------------------------
            //  Build the packet
            //------------------------------------

            List<byte[]> packet = new List<byte[]>();

            //  The keepalive identifier
            packet.Add(Encoding.UTF8.GetBytes("K"));

            //  The username length
            //  NOTE: I am casting an int into short because the maximum username length is 50;
            //  so even a 16 bits is too much
            short length = (short)username.Length;
            packet.Add(BitConverter.GetBytes(length));

            //  The username
            packet.Add(username);
            
            if (!PicturePath.Equals(String.Empty))
            {
                //  The pictures MD5
                //  NOTE: I know MD5 is not recommended as it has been proven to cause collisions easily,
                //  but in my case, I use it just to check if the picture is the same as before.
                //  Also it is 16 bytes long, and I am using UDP, so I need to keep it short
                packet.Add(PictureMD5);

                //  The picture
                packet.Add(PictureBitMap);
            }            

            //  Finally, make the packet
            KeepAlivePacket = packet.SelectMany(bytes => bytes).ToArray();
        }

        /// <summary>
        /// Update the settings.
        /// Values should have been validated by the View Model
        /// </summary>
        /// <param name="NewName">The new username to set</param>
        /// <param name="NewDownloadsPath">New downloads path</param>
        /// <param name="NewPicturePath">New profile picture path</param>
        /// <param name="NewAutoAccept">New autoaccept parameter</param>
        /// <returns>True on success, false on failure</returns>
        /// <remarks>Would probably be better to throw exceptions, rather than returning bool</remarks>
        public bool Update(string NewName, string NewDownloadsPath, string NewPicturePath, bool NewAutoAccept)
        {
            //-----------------------------------
            //  Update Registry
            //-----------------------------------

            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"software\pds", true))
                {
                    k.SetValue("username", NewName, RegistryValueKind.String);
                    k.SetValue("path", NewDownloadsPath, RegistryValueKind.String);
                    k.SetValue("userpic", NewPicturePath, RegistryValueKind.String);
                    k.SetValue("autoaccept", NewAutoAccept == true ? 1 : 0, RegistryValueKind.DWord);
                }
            }
            catch(Exception)
            {
                return false;
            }

            //-----------------------------------
            //  Update Class
            //-----------------------------------

            Name = NewName;
            DefaultSavePath = NewDownloadsPath;
            PicturePath = NewPicturePath;
            AutoAccept = NewAutoAccept;

            //  Re-calculate keep alive packet
            CalculateKeepAlivePacket();

            return true;
        }

        /// <summary>
        /// Get the thumbnail of the profile picture
        /// </summary>
        /// <remarks>This should be done by a Picture Model</remarks>
        private void GetThumbnail()
        {
            if (PicturePath.Equals(String.Empty)) return;
            decimal width, height, resize;
            try
            {
                using (Image image = Image.FromFile(PicturePath))
                {
                    //  This is a very simple proportions-aware resizing.
                    //  It shouldn't be done like this because I round results at the same time,
                    //  instead I should round one and then update the proportions later
                    width = image.Width;
                    height = image.Height;
                    resize = width < height ? 70 / width : 70 / height;
                    width = width * resize;
                    height = height * resize;
                    System.Drawing.Imaging.ImageFormat format = image.RawFormat;

                    //-----------------------------------
                    //  Get the thumbnail 
                    //-----------------------------------

                    using (Bitmap bitmap = new Bitmap(image, new Size((int)Math.Round(width), (int)Math.Round(height))))
                    {
                        using (MemoryStream stream = new MemoryStream())
                        {
                            bitmap.Save(stream, format);

                            //  Save it here
                            PictureBitMap = stream.ToArray();

                            //  Calculate MD5
                            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
                            {
                                stream.Position = 0;
                                PictureMD5 = md5.ComputeHash(stream);
                            }
                        }                            
                    }                        
                }
            }
            catch(Exception)
            {
                return;
            }            
        }

        ~Settings()
        {
            DestroyContextMenu();
        }
    }
}
