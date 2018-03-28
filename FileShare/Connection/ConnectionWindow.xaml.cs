using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Net.Sockets;

namespace FileShare
{
    /// <summary>
    /// Interaction logic for ConnectionWindow.xaml
    /// </summary>
    public partial class ConnectionWindow : Window
    {
        IncomingTransferViewModel ViewModel;

        public ConnectionWindow(TcpClient client)
        {
            //InitializeComponent();
            ViewModel = new IncomingTransferViewModel(this, client);
            //DataContext = ViewModel;
        }

        /// <summary>
        /// When closing the window, you have to automatically reject the transfer request
        /// </summary>
        /// <param name="sender">The sender</param>
        /// <param name="e">The event arguments</param>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //  NOTE: if you click on the Reject transfer button, this function will be called too,
            //  calling reject a second time. But this time, window.close won't be called again.
            ViewModel.Cancel(true);
        }
    }
}
