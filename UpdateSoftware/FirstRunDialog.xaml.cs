using System.Windows;

namespace UpdateSoftware
{
    public partial class FirstRunDialog : Window
    {
        public bool CloseToTray { get; private set; }

        public FirstRunDialog()
        {
            InitializeComponent();
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            CloseToTray = false;
            DialogResult = true;
            Close();
        }

        private void BtnTray_Click(object sender, RoutedEventArgs e)
        {
            CloseToTray = true;
            DialogResult = true;
            Close();
        }
    }
}
