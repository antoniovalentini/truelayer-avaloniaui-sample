using MobileApp.ViewModels;
using Xunit;

namespace MobileApp.Tests;

public class TransactionsViewModelTests
{
    [Fact]
    public void Constructor_ExposesGivenAccountFields()
    {
        var viewModel = new TransactionsViewModel("GB00TEST00000000000000", "£100.00", "£120.00", "£0.00");

        Assert.Equal("GB00TEST00000000000000", viewModel.Iban);
        Assert.Equal("£100.00", viewModel.AvailableAmount);
        Assert.Equal("£120.00", viewModel.CurrentAmount);
        Assert.Equal("£0.00", viewModel.Overdraft);
    }
}
