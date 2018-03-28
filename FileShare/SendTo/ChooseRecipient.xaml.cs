using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;

namespace FileShare
{
    /// <summary>
    /// Interaction logic for ChooseRecipient.xaml
    /// </summary>
    public partial class ChooseRecipient : UserControl
    {
        ChooseRecipientViewModel ViewModel;

        public ChooseRecipient()
        {
            InitializeComponent();
            ViewModel = new ChooseRecipientViewModel();
            DataContext = ViewModel;
        }
    }

    /// <summary>
    /// The View Model
    /// </summary>
    public class ChooseRecipientViewModel
    {
        /// <summary>
        /// Users, gotten from the Application 
        /// </summary>
        public ObservableCollection<User> Users { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public ChooseRecipientViewModel()
        {
            Users = (ObservableCollection<User>)Application.Current.Properties["Users"];
        }
    }
}
