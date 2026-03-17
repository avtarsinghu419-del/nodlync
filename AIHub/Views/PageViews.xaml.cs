using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;

namespace AIHub.Views
{
    public partial class DashboardPage : UserControl
    {
        public DashboardPage() { InitializeComponent(); }
    }
    public partial class ApiVaultPage : UserControl
    {
        public ApiVaultPage() { InitializeComponent(); }

        private void ProductNameTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            // Automatically focus the first input when the add-key interface opens
            if (sender is Control control)
            {
                control.Focus();
                Keyboard.Focus(control);
            }
        }
    }
    public partial class ApiTesterPage : UserControl
    {
        public ApiTesterPage() { InitializeComponent(); }
    }
    public partial class WorkflowsPage : UserControl
    {
        public WorkflowsPage() { InitializeComponent(); }
    }
    public partial class ReportsPage : UserControl
    {
        public ReportsPage() { InitializeComponent(); }
    }
    public partial class MeetingsPage : UserControl
    {
        public MeetingsPage() { InitializeComponent(); }
    }
    public partial class LogsPage : UserControl
    {
        public LogsPage() { InitializeComponent(); }
    }
    public partial class IdeasLabPage : UserControl
    {
        public IdeasLabPage() { InitializeComponent(); }
    }
    public partial class ProfilePage : UserControl
    {
        public ProfilePage() { InitializeComponent(); }
    }
}
