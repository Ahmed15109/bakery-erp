namespace Bakery.Domain.Enums;

public enum PayrollStatus
{
    Draft = 1,
    Calculated = 2,
    Paid = 3,
    Cancelled = 4
}

public enum EmployeeTransactionType
{
    Earned = 1,      // Credit to employee (e.g. from production)
    Advance = 2,     // Debit from employee (money taken)
    Bonus = 3,       // Credit to employee
    Deduction = 4,   // Debit from employee
    SalaryPayment = 5 // Actual final salary payout
}
