using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AIHub.Models;
using AIHub.ViewModels;

namespace AIHub.Views
{
    public partial class ProjectsPage : UserControl
    {
        public ProjectsPage()
        {
            InitializeComponent();
        }

        private void NewProjectButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ProjectsViewModel vm)
            {
                Console.WriteLine("NewProjectButton_Click");
                if (vm.NewProjectCommand.CanExecute(null))
                {
                    vm.NewProjectCommand.Execute(null);
                }
            }
        }

        private void ProjectListItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) != null)
            {
                return;
            }

            if (DataContext is not ProjectsViewModel vm || sender is not ListViewItem item || item.DataContext is not Project project)
            {
                return;
            }

            item.IsSelected = true;
            vm.SelectedProject = project;

            if (vm.OpenWorkspaceCommand.CanExecute(project))
            {
                vm.OpenWorkspaceCommand.Execute(project);
            }
        }

        private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
        {
            var current = source;
            while (current != null)
            {
                if (current is T match)
                {
                    return match;
                }

                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }
}
