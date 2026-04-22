#nullable enable
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Kroira.App.Services
{
    public interface IRemoteNavigationPage
    {
        bool TryFocusPrimaryTarget();
        bool TryHandleBackRequest();
    }

    public static class RemoteNavigationHelper
    {
        public static bool TryFocusElement(Control? control, FocusState focusState = FocusState.Keyboard)
        {
            if (control == null || control.XamlRoot == null || !control.IsEnabled || control.Visibility != Visibility.Visible)
            {
                return false;
            }

            return control.Focus(focusState);
        }

        public static bool TryFocusListItem(ListViewBase? list, int preferredIndex = 0, FocusState focusState = FocusState.Keyboard)
        {
            if (list == null || list.XamlRoot == null || list.Visibility != Visibility.Visible || list.Items.Count == 0)
            {
                return false;
            }

            var clampedIndex = Math.Max(0, Math.Min(preferredIndex, list.Items.Count - 1));
            var item = list.Items[clampedIndex];
            list.ScrollIntoView(item);
            list.UpdateLayout();

            if (list.ContainerFromIndex(clampedIndex) is Control container &&
                container.Visibility == Visibility.Visible &&
                container.IsEnabled)
            {
                return container.Focus(focusState);
            }

            return list.Focus(focusState);
        }

        public static T? FindDataContextInAncestors<T>(object source) where T : class
        {
            var current = source as DependencyObject;
            while (current != null)
            {
                if (current is FrameworkElement { DataContext: T match })
                {
                    return match;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        public static bool IsWithinInteractiveControl(object source)
        {
            var current = source as DependencyObject;
            while (current != null)
            {
                if (current is ButtonBase or HyperlinkButton or ToggleSwitch or CheckBox or ComboBox or ComboBoxItem or Slider or TextBox or PasswordBox)
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        public static bool IsDescendantOf(DependencyObject? source, DependencyObject? ancestor)
        {
            var current = source;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }
    }
}
