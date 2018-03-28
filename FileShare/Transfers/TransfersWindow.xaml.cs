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
using System.Collections.ObjectModel;

namespace FileShare
{
    /// <summary>
    /// Interaction logic for TransfersWindow.xaml
    /// </summary>
    public partial class TransfersWindow : Window
    {
        TransferViewModel ViewModel;

        public TransfersWindow()
        {
            InitializeComponent();
            ViewModel = new TransferViewModel();
            DataContext = ViewModel;
        }

        /// <summary>
        /// Cancel the current transfer
        /// </summary>
        /// <param name="sender">the sender</param>
        /// <param name="e">the arguments</param>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //  hide the window if you clicked the X
            if(!(bool)Application.Current.Properties["Exiting"])
            {
                e.Cancel = true;
                this.Hide();
                return;
            }

            //  Ok close yourself
        }
    }
}
