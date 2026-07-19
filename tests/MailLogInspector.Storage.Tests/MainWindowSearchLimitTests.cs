using System.Reflection;
using System.Windows.Controls;
using MailLogInspector.App;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class MainWindowSearchLimitTests
{
    [Fact]
    public void SearchLimitSentinel_All_IsNotReplacedByTheFiveHundredFallback()
    {
        int? parsedLimit = null;
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                var comboBox = new ComboBox();
                comboBox.Items.Add(new ComboBoxItem { Content = "alles", Tag = "-1" });
                comboBox.SelectedIndex = 0;

                MethodInfo method = typeof(MainWindow).GetMethod(
                    "ReadComboTagAsInt",
                    BindingFlags.NonPublic | BindingFlags.Static)!;

                parsedLimit = Assert.IsType<int>(method.Invoke(null, [comboBox, 500]));
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(exception);
        Assert.Equal(-1, parsedLimit);
    }
}
