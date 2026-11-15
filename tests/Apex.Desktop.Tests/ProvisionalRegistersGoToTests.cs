using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Wave-10 review (track gd) — census 5.7 / 5.8, the vendor's Alt+G route to the two provisional registers.</b>
/// The branch added the Gateway &gt; Exception Reports rows and stated in a comment that, because Go To's index is
/// walked from that menu, the two labels also reach the Alt+G list; no test pressed Alt+G for either.
/// <para>Vendor (R7, ruling 14), <c>help.tallysolutions.com/tally-prime/accounting/accounting-entry-tally/</c>, opened
/// by content 2026-11-02: <i>"Press Alt+G (Go to) &gt; type or select Optional Vouchers &gt; and press Enter."</i>
/// and the same sentence for <i>Post-Dated Vouchers</i>.</para>
/// <para><b>Mutation (measured):</b> deleting the <c>"Optional Vouchers"</c> row from the Exception Reports column
/// in <c>MainWindowViewModel</c> drops the label from the Go To index and reddens the Optional case.</para>
/// </summary>
public sealed class ProvisionalRegistersGoToTests
{
    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData("Optional Vouchers", ReportKind.OptionalVouchersRegister)]
    [InlineData("Post-Dated Vouchers", ReportKind.PostDatedVouchersRegister)]
    public void Alt_G_Go_To_opens_the_provisional_register_by_name(string label, ReportKind expected)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexProvGoTo_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        vm.NewCompanyName = "Provisional GoTo Co";
        vm.CreateCompany();
        var window = new MainWindow { DataContext = vm, Width = 1366, Height = 768 };
        window.Show();
        try
        {
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Alt);
            Pump(window);
            var box = window.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "GoToSearchBox");
            box.Text = label;
            Pump(window);
            Assert.Equal(label, vm.GoTo!.Selected!.Label);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.Equal(expected, vm.Reports!.Kind);
        }
        finally
        {
            window.Close();
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
