using System.Windows;
using CdsHelper.Support.UI.Units;

namespace CdsHelper.Navigation.UI.Views;

public class NavigationMenu : AccordionControl
{
    static NavigationMenu()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(NavigationMenu),
            new FrameworkPropertyMetadata(typeof(NavigationMenu)));
    }
}
