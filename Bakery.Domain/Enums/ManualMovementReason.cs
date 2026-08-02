namespace Bakery.Domain.Enums;

public enum ManualMovementReason
{
    OwnerCapital = 1,
    OwnerWithdrawal = 2,
    BankDeposit = 3,
    BankWithdrawal = 4,
    CashAdjustment = 5,
    TransferCorrection = 6,
    Emergency = 7,
    Other = 8
}
