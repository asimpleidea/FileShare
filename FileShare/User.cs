using System;
using System.Net;
using System.IO;
using System.ComponentModel;

namespace FileShare
{
    /// <summary>
    /// The User Model
    /// This will provide all user's infos
    /// </summary>
    /// <remarks>
    /// Implements INotifyPropertyChanged, so changes are already propagated to the view
    /// I usually don't like implementing INotifyPropertyChanged on Models but just on View Models,
    /// but I don't want to create a view model (no overheads)
    /// </remarks>
    public class User : INotifyPropertyChanged
    {
        /// <summary>
        /// The User's name
        /// </summary>
        private string name = String.Empty;
        public string Name
        {
            get { return name; }
            set
            {
                if (!name.Equals(value)) name = value;
                PropertyHasChanged(nameof(Name));
            }
        }

        /// <summary>
        /// The user's IP address. Used to know who to send the file to.
        /// </summary>
        /// <remarks>
        /// No need to raise events here.
        /// </remarks>
        public IPAddress IP { get; set; }

        //public string Pic { get; set; }
        /// <summary>
        /// The user's picture, as a stream of bytes.
        /// </summary>
        /// <remarks>
        /// The first implementation was with a path of the image.
        /// I switched it to a MemoryStream so I don't lose any other time checking if picture already exists,
        /// deleting the previous one, wasting time to save it etc. Also pictures will be formatted.
        /// </remarks>
        private MemoryStream picbytes = new MemoryStream();
        public  byte[] PicBytes
        {
            get { return picbytes.ToArray(); }
            set
            {
                if (value != null && value.Length > 1)
                {
                    picbytes = new MemoryStream(value);
                    PropertyHasChanged(nameof(PicBytes));
                }
            }
        }

        /// <summary>
        /// The picture's MD5
        /// Used to check if user changed the picture or it is always the same.
        /// </summary>
        /// <remarks>
        /// For files, I SHA256-ed the file and here I MD5-ed the picture, why?
        /// Because I am using UDP to send the picture: I want to occupy as little space as possible (64K MAX DATAGRAM SIZE),
        /// *very* small collision probability in this case (it will be checked only against this user's picture),
        /// and no dangers if collision occurred (picture just won't change).
        /// </remarks>
        public byte[] PictureMD5 { get; set; } = null;

        /// <summary>
        /// Last time I received this keep alive packet
        /// </summary>
        public DateTime Time { get; set; }

        /// <summary>
        /// The constructor
        /// </summary>
        public User()
        {
            Time = DateTime.Now;
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="user">The user I need to copy from</param>
        /// <remarks>
        /// I create a new user based on that one, because a user could exit and will be deleted by the collection.
        /// When that happens, errors might occur on the transfer window (could not find the user because of NullReference etc).
        /// </remarks>
        public User(User user)
        {
            //  Just copy stuff, man
            Name = user.Name;
            IP = user.IP;
            PicBytes = user.PicBytes;
            PictureMD5 = user.PictureMD5;
            Time = user.Time;
        }

        /// <summary>
        /// Checks if Picture needs to be updated
        /// </summary>
        /// <param name="newMD5">The new MD5 to check against</param>
        /// <returns>True if picture is new, false if not or error occurred</returns>
        public bool PictureNeedsUpdate(byte[] newMD5 = null)
        {
            //  Can't check if null or md5 is not valid
            if (newMD5 == null || newMD5.Length != 16) return false;

            //  If current PictureMD5 is null (default picture), then Yes: picture needs to be updated.
            if (PictureMD5 == null)
            {
                PictureMD5 = new byte[16];
                newMD5.CopyTo(PictureMD5, 0);
                return true;
            }

            //------------------------------------
            //  Check the two MD5s
            //------------------------------------

            int i;
            for (i = 0; i < 16 && PictureMD5[i] == newMD5[i]; ++i) ;
            if (i != 16)
            {
                //  No need to new byte[16], because if you're here, it means this is at least the second time,
                //  all the above have already been executed
                newMD5.CopyTo(PictureMD5, 0);
                return true;
            }

            //  Default
            return false;
        }

        /// <summary>
        /// The event handler
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// The function that will propagate the event
        /// </summary>
        /// <param name="propertyName">Who called it</param>
        public void PropertyHasChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// The destructor
        /// </summary>
        ~User()
        {
            //  Dispose the MemoryStream
            picbytes.Dispose();
        }
    }
}
