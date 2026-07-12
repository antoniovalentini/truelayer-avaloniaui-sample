namespace MobileApp.ViewModels;

public class TransactionsViewModel(string iban, string availableAmount, string currentAmount, string overdraft)
{
    public string Iban { get; } = iban;
    public string AvailableAmount { get; } = availableAmount;
    public string CurrentAmount { get; } = currentAmount;
    public string Overdraft { get; } = overdraft;
}
