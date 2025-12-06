using System.Windows;
using System.Windows.Controls;

namespace CdsHelper.Main.UI.Views;

public class CharacterContent : ContentControl
{
    static CharacterContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(CharacterContent),
            new FrameworkPropertyMetadata(typeof(CharacterContent)));
    }
}
