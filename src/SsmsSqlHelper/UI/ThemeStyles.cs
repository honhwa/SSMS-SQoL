using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace SsmsSqlHelper.UI
{
    /// <summary>
    /// Our windows are plain WPF, so they take the colours of the current SSMS theme (light or dark) through the shell's
    /// brush keys. Done in code so a wrong key fails the build instead of the window.
    /// </summary>
    internal static class ThemeStyles
    {
        /// <summary>Call before <c>InitializeComponent()</c> so the implicit styles are there when the XAML is loaded.</summary>
        public static void Apply(Window window)
        {
            window.SetResourceReference(Control.BackgroundProperty, VsBrushes.WindowKey);
            window.SetResourceReference(Control.ForegroundProperty, VsBrushes.WindowTextKey);

            window.Resources[typeof(TextBox)] = ThemedStyle(typeof(TextBox),
                (Control.BackgroundProperty, CommonControlsColors.TextBoxBackgroundBrushKey),
                (Control.ForegroundProperty, CommonControlsColors.TextBoxTextBrushKey),
                (Control.BorderBrushProperty, CommonControlsColors.TextBoxBorderBrushKey),
                (TextBoxBase.CaretBrushProperty, CommonControlsColors.TextBoxTextBrushKey));

            window.Resources[typeof(ListBox)] = ThemedStyle(typeof(ListBox),
                (Control.BackgroundProperty, CommonControlsColors.TextBoxBackgroundBrushKey),
                (Control.ForegroundProperty, CommonControlsColors.TextBoxTextBrushKey),
                (Control.BorderBrushProperty, CommonControlsColors.TextBoxBorderBrushKey));

            window.Resources[typeof(CheckBox)] = ThemedStyle(typeof(CheckBox),
                (Control.ForegroundProperty, VsBrushes.WindowTextKey));

            window.Resources[typeof(Button)] = ButtonStyle();
        }

        internal static Style ThemedStyle(Type target, params (DependencyProperty Property, object Key)[] setters)
        {
            var style = new Style(target);
            foreach (var (property, key) in setters)
                style.Setters.Add(new Setter(property, new DynamicResourceExtension(key))); // Setter turns this into a resource reference when sealed
            return style;
        }

        // The stock button template paints its own light-blue hover colour, unreadable on a dark theme; this one only dims instead
        internal static Style ButtonStyle()
        {
            var style = ThemedStyle(typeof(Button),
                (Control.BackgroundProperty, CommonControlsColors.ButtonBrushKey),
                (Control.ForegroundProperty, CommonControlsColors.ButtonTextBrushKey),
                (Control.BorderBrushProperty, CommonControlsColors.ButtonBorderBrushKey));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 4, 14, 4)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);

            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            template.Triggers.Add(new Trigger { Property = UIElement.IsMouseOverProperty, Value = true, Setters = { new Setter(UIElement.OpacityProperty, 0.8) } });
            template.Triggers.Add(new Trigger { Property = ButtonBase.IsPressedProperty, Value = true, Setters = { new Setter(UIElement.OpacityProperty, 0.6) } });
            template.Triggers.Add(new Trigger { Property = UIElement.IsEnabledProperty, Value = false, Setters = { new Setter(UIElement.OpacityProperty, 0.45) } });
            template.Triggers.Add(new Trigger { Property = Button.IsDefaultProperty, Value = true, Setters = { new Setter(Control.BorderThicknessProperty, new Thickness(2)) } });

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }
    }
}
