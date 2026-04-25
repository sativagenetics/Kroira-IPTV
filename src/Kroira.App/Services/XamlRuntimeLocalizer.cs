#nullable enable

using System;
using System.Reflection;
using Kroira.App.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Kroira.App.Services
{
    public static class RuntimeLocalizer
    {
        public static readonly DependencyProperty UidProperty =
            DependencyProperty.RegisterAttached(
                "Uid",
                typeof(string),
                typeof(RuntimeLocalizer),
                new PropertyMetadata(string.Empty));

        public static string GetUid(DependencyObject element)
        {
            return element.GetValue(UidProperty) as string ?? string.Empty;
        }

        public static void SetUid(DependencyObject element, string value)
        {
            element.SetValue(UidProperty, value ?? string.Empty);
        }
    }

    public static class XamlRuntimeLocalizer
    {
        public static void Apply(DependencyObject? root)
        {
            if (root == null)
            {
                return;
            }

            ApplyElement(root);
        }

        private static void ApplyElement(DependencyObject element)
        {
            ApplyUidResources(element);

            if (element is TextBlock textBlock)
            {
                foreach (var inline in textBlock.Inlines)
                {
                    if (inline is DependencyObject inlineElement)
                    {
                        ApplyUidResources(inlineElement);
                    }
                }
            }

            ApplyFlyoutResources(element);

            var childCount = VisualTreeHelper.GetChildrenCount(element);
            for (var index = 0; index < childCount; index++)
            {
                ApplyElement(VisualTreeHelper.GetChild(element, index));
            }
        }

        private static void ApplyUidResources(DependencyObject element)
        {
            var uid = RuntimeLocalizer.GetUid(element);
            if (string.IsNullOrWhiteSpace(uid))
            {
                return;
            }

            switch (element)
            {
                case TextBlock textBlock:
                    TryApplyStringProperty(textBlock, uid, nameof(TextBlock.Text));
                    break;
                case Run run:
                    TryApplyStringProperty(run, uid, nameof(Run.Text));
                    break;
                case ContentControl contentControl:
                    TryApplyObjectProperty(contentControl, uid, nameof(ContentControl.Content));
                    break;
                case LoadingStateView loading:
                    TryApplyStringProperty(loading, uid, nameof(LoadingStateView.Title));
                    TryApplyStringProperty(loading, uid, nameof(LoadingStateView.Message));
                    break;
                case EmptyStateView empty:
                    TryApplyStringProperty(empty, uid, nameof(EmptyStateView.Title));
                    TryApplyStringProperty(empty, uid, nameof(EmptyStateView.Message));
                    TryApplyStringProperty(empty, uid, nameof(EmptyStateView.PrimaryActionLabel));
                    TryApplyStringProperty(empty, uid, nameof(EmptyStateView.SecondaryActionLabel));
                    break;
                case ErrorStateView error:
                    TryApplyStringProperty(error, uid, nameof(ErrorStateView.Title));
                    TryApplyStringProperty(error, uid, nameof(ErrorStateView.Message));
                    TryApplyStringProperty(error, uid, nameof(ErrorStateView.RetryActionLabel));
                    TryApplyStringProperty(error, uid, nameof(ErrorStateView.DiagnosticsActionLabel));
                    break;
            }

            TryApplyObjectProperty(element, uid, "Header");
            TryApplyStringProperty(element, uid, "PlaceholderText");
            TryApplyStringProperty(element, uid, "Label");
            TryApplyObjectProperty(element, uid, "OnContent");
            TryApplyObjectProperty(element, uid, "OffContent");
            TryApplyObjectProperty(element, uid, "Text");
            TryApplyToolTip(element, uid);
            TryApplyAutomationName(element, uid);
        }

        private static void ApplyFlyoutResources(DependencyObject element)
        {
            ApplyFlyoutFromProperty(element, "Flyout");
            if (element is FrameworkElement frameworkElement && frameworkElement.ContextFlyout != null)
            {
                ApplyFlyout(frameworkElement.ContextFlyout);
            }
        }

        private static void ApplyFlyoutFromProperty(object target, string propertyName)
        {
            var property = target.GetType().GetRuntimeProperty(propertyName);
            if (property?.GetValue(target) is FlyoutBase flyout)
            {
                ApplyFlyout(flyout);
            }
        }

        private static void ApplyFlyout(FlyoutBase flyout)
        {
            ApplyUidResources(flyout);

            if (flyout is MenuFlyout menuFlyout)
            {
                foreach (var item in menuFlyout.Items)
                {
                    ApplyMenuFlyoutItem(item);
                }
            }
        }

        private static void ApplyMenuFlyoutItem(MenuFlyoutItemBase item)
        {
            ApplyUidResources(item);
            if (item is MenuFlyoutSubItem subItem)
            {
                foreach (var child in subItem.Items)
                {
                    ApplyMenuFlyoutItem(child);
                }
            }
        }

        private static void TryApplyToolTip(DependencyObject element, string uid)
        {
            if (!LocalizedStrings.TryGet(string.Concat(uid, ".ToolTipService.ToolTip"), out var value))
            {
                return;
            }

            ToolTipService.SetToolTip(element, value);
        }

        private static void TryApplyAutomationName(DependencyObject element, string uid)
        {
            if (LocalizedStrings.TryGet(
                    string.Concat(uid, ".[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name"),
                    out var value))
            {
                AutomationProperties.SetName(element, value);
            }
        }

        private static void TryApplyStringProperty(object target, string uid, string propertyName)
        {
            if (!LocalizedStrings.TryGet(string.Concat(uid, ".", propertyName), out var value))
            {
                return;
            }

            var property = target.GetType().GetRuntimeProperty(propertyName);
            if (property?.CanWrite == true && property.PropertyType == typeof(string))
            {
                property.SetValue(target, value);
            }
        }

        private static void TryApplyObjectProperty(object target, string uid, string propertyName)
        {
            if (!LocalizedStrings.TryGet(string.Concat(uid, ".", propertyName), out var value))
            {
                return;
            }

            var property = target.GetType().GetRuntimeProperty(propertyName);
            if (property?.CanWrite != true)
            {
                return;
            }

            if (property.PropertyType == typeof(string))
            {
                property.SetValue(target, value);
                return;
            }

            if (property.PropertyType == typeof(object))
            {
                var current = property.GetValue(target);
                if (current == null || current is string)
                {
                    property.SetValue(target, value);
                }
            }
        }
    }
}
