using System.Windows;
using System.Windows.Controls;

namespace UpdateSoftware
{
    public partial class LoginDialog : Window
    {
        public bool IsAuthenticated { get; private set; }
        public string UserName { get; private set; }

        public LoginDialog()
        {
            InitializeComponent();
            TxtUserName.Focus();
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string user = TxtUserName.Text.Trim();
            string pass = PwdPassword.Password;

            if (string.IsNullOrEmpty(user))
            {
                TxtError.Text = "请输入用户名";
                TxtError.Visibility = Visibility.Visible;
                TxtUserName.Focus();
                return;
            }

            if (string.IsNullOrEmpty(pass))
            {
                TxtError.Text = "请输入密码";
                TxtError.Visibility = Visibility.Visible;
                PwdPassword.Focus();
                return;
            }

            // TODO: 可根据实际需要对接账号验证逻辑
            // 当前仅做非空校验，可通过在代码中修改增加密码校验
            IsAuthenticated = true;
            UserName = user;
            DialogResult = true;
            Close();
        }
    }
}
