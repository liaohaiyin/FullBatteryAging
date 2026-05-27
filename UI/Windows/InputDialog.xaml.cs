using System.Windows;

namespace BatteryAging.UI.Windows
{
    public partial class InputDialog : Window
    {
        public string Prompt
        {
            get => PromptText.Text;
            set => PromptText.Text = value;
        }

        public string InputText
        {
            get => InputTextBox.Text;
            set => InputTextBox.Text = value;
        }

        public InputDialog()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                InputTextBox.Focus();
                InputTextBox.SelectAll();
            };
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
