using System.Collections.ObjectModel;
using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bakery.WPF.ViewModels;

public sealed partial class EmployeeLedgerViewModel : ViewModelBase
{
    private readonly ISettlementService _settlementService;
    private readonly IMessageService _messageService;
    private readonly ISafeContext _safeContext;

    public EmployeeLedgerViewModel(
        ISettlementService settlementService,
        IMessageService messageService,
        ISafeContext safeContext)
    {
        _settlementService = settlementService;
        _messageService = messageService;
        _safeContext = safeContext;
        Title = "كشف حساب الموظف";
        LedgerEntries = [];
    }

    [ObservableProperty] private Employee? employee;
    [ObservableProperty] private decimal currentBalance;
    
    [ObservableProperty] private decimal totalEarned;
    [ObservableProperty] private decimal totalPaid;
    [ObservableProperty] private decimal totalBonuses;
    [ObservableProperty] private decimal totalDeductions;

    public ObservableCollection<LedgerEntryViewModel> LedgerEntries { get; }

    public async Task InitializeAsync(Employee selectedEmployee)
    {
        Employee = selectedEmployee;
        await LoadLedger();
    }

    [RelayCommand]
    private async Task LoadLedger()
    {
        if (Employee == null) return;

        var transactions = await _settlementService.GetEmployeeStatementAsync(Employee.Id);
        LedgerEntries.Clear();

        decimal runningBalance = 0;
        TotalEarned = 0;
        TotalPaid = 0;
        TotalBonuses = 0;
        TotalDeductions = 0;

        foreach (var tx in transactions.OrderBy(t => t.Date).ThenBy(t => t.Id))
        {
            decimal debit = 0;
            decimal credit = 0;

            switch (tx.Type)
            {
                case EmployeeTransactionType.Earned:
                    debit = tx.Amount;
                    runningBalance += tx.Amount;
                    TotalEarned += tx.Amount;
                    break;
                case EmployeeTransactionType.Bonus:
                    debit = tx.Amount;
                    runningBalance += tx.Amount;
                    TotalBonuses += tx.Amount;
                    break;
                case EmployeeTransactionType.Advance:
                case EmployeeTransactionType.SalaryPayment:
                    credit = tx.Amount;
                    runningBalance -= tx.Amount;
                    TotalPaid += tx.Amount;
                    break;
                case EmployeeTransactionType.Deduction:
                    credit = tx.Amount;
                    runningBalance -= tx.Amount;
                    TotalDeductions += tx.Amount;
                    break;
            }

            LedgerEntries.Add(new LedgerEntryViewModel(
                tx.Date,
                TranslateType(tx.Type),
                tx.Notes ?? string.Empty,
                debit,
                credit,
                runningBalance,
                tx.Type
            ));
        }

        CurrentBalance = runningBalance;
        
        var reversed = LedgerEntries.Reverse().ToList();
        LedgerEntries.Clear();
        foreach(var entry in reversed) LedgerEntries.Add(entry);
    }

    [RelayCommand]
    private async Task AddAdvance() => await AddQuickTransaction(EmployeeTransactionType.Advance, "سحب مقدم / عهدة");

    [RelayCommand]
    private async Task AddBonus() => await AddQuickTransaction(EmployeeTransactionType.Bonus, "إضافة مكافأة");

    [RelayCommand]
    private async Task AddDeduction() => await AddQuickTransaction(EmployeeTransactionType.Deduction, "إضافة خصم");

    [RelayCommand]
    private async Task PayEmployee() => await AddQuickTransaction(EmployeeTransactionType.SalaryPayment, "صرف راتب / مستحقات");

    private async Task AddQuickTransaction(EmployeeTransactionType type, string title)
    {
        if (Employee == null) return;

    
        
        var amountStr = await _messageService.ShowInputAsync(title, "أدخل المبلغ:", "0");
        if (string.IsNullOrEmpty(amountStr) || !decimal.TryParse(amountStr, out decimal amount) || amount <= 0) return;

        var notes = await _messageService.ShowInputAsync(title, "ملاحظات:", "");

        var tx = new EmployeeTransaction
        {
            EmployeeId = Employee.Id,
            Type = type,
            Amount = amount,
            Date = DateTime.Now,
            Notes = notes
        };

        try
        {
            int? safeId = null;
            if (type == EmployeeTransactionType.Advance || type == EmployeeTransactionType.SalaryPayment)
            {
                if (!_safeContext.CurrentSafeId.HasValue)
                {
                    _messageService.ShowError("لا توجد خزنة نشطة حالياً. يرجى اختيار خزنة أولاً.");
                    return;
                }
                safeId = _safeContext.CurrentSafeId.Value;
            }

            await _settlementService.AddTransactionAsync(tx, safeId);
            _messageService.ShowInfo("تمت العملية بنجاح");
            await LoadLedger();
        }
        catch (Exception ex)
        {
            _messageService.ShowError(Bakery.WPF.Logging.OperatorErrorHandler.LogAndTranslate(ex, "Employee ledger operation"));
        }
    }

    private string TranslateType(EmployeeTransactionType type)
    {
        return type switch
        {
            EmployeeTransactionType.Earned => "استحقاق",
            EmployeeTransactionType.Advance => "سحب / مقدم",
            EmployeeTransactionType.Bonus => "مكافأة",
            EmployeeTransactionType.Deduction => "خصم",
            EmployeeTransactionType.SalaryPayment => "صرف مستحقات",
            _ => type.ToString()
        };
    }

    [RelayCommand]
    private void ExportPdf()
    {
        _messageService.ShowInfo("جاري تصدير كشف الحساب إلى PDF...");
    }

    [RelayCommand]
    private void Print()
    {
        _messageService.ShowInfo("جاري تحضير الطباعة...");
    }
}

public record LedgerEntryViewModel(
    DateTime Date,
    string Type,
    string Description,
    decimal Debit,
    decimal Credit,
    decimal RunningBalance,
    EmployeeTransactionType RawType);
