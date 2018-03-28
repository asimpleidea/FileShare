using System.Windows;

namespace FileShare
{
    /// <summary>
    /// Interaction logic for SendToWindow.xaml
    /// </summary>
    public partial class SendToWindow : Window
    {
        SendToViewModel ViewModel;

        public SendToWindow(string path)
        {
            InitializeComponent();
            ViewModel = new SendToViewModel(this, path);
            DataContext = ViewModel;
        }
    }
}