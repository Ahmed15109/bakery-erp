using Bakery.Domain.Enums;
using Bakery.Domain.Interfaces;
using System;
using System.Collections.Generic;

namespace Bakery.Domain.Entities;

public sealed class Party : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public PartyType Type { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? NationalId { get; set; }
    public string? TaxNumber { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<PartyLedgerEntry> LedgerEntries { get; set; } = [];
    public ICollection<PurchaseInvoice> PurchaseInvoices { get; set; } = [];
    public ICollection<SaleInvoice> SaleInvoices { get; set; } = [];
}

public sealed class PartyLedgerEntry : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public int WorkingDayId { get; set; }
    public WorkingDay WorkingDay { get; set; } = null!;
    public int PartyId { get; set; }
    public Party Party { get; set; } = null!;
    public DateTime EntryDate { get; set; } = DateTime.UtcNow;
    public decimal Amount { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ReferenceType { get; set; }
    public int? ReferenceId { get; set; }
    public int? ReversalReferenceId { get; set; }
    public bool IsReversed { get; set; }
    public int? SourceSafeMovementId { get; set; }
    public SafeMovement? SourceSafeMovement { get; set; }

    public (decimal IncreaseAmount, decimal DecreaseAmount) GetAccountingImpact(Enums.PartyType partyType)
    {
        if (IsReversed) return (0, 0);

        if (partyType == Enums.PartyType.Customer)
        {
            return ReferenceType switch
            {
                Bakery.Domain.Constants.LedgerReferenceTypes.SaleInvoice => (Debit, Credit),
                Bakery.Domain.Constants.LedgerReferenceTypes.SaleCancel => (0, 0),
                Bakery.Domain.Constants.LedgerReferenceTypes.CustomerReceipt => (0, Credit),
                null or "" => (Debit, Credit),
                _ => (0, 0)
            };
        }
        else if (partyType == Enums.PartyType.Supplier)
        {
            return ReferenceType switch
            {
                Bakery.Domain.Constants.LedgerReferenceTypes.PurchaseInvoice => (Credit, Debit),
                Bakery.Domain.Constants.LedgerReferenceTypes.PurchaseCancel => (0, 0),
                Bakery.Domain.Constants.LedgerReferenceTypes.SupplierPayment => (0, Debit),
                null or "" => (Credit, Debit),
                _ => (0, 0)
            };
        }
        else
        {
            return ReferenceType switch
            {
                Bakery.Domain.Constants.LedgerReferenceTypes.SaleInvoice => (Debit, Credit),
                Bakery.Domain.Constants.LedgerReferenceTypes.CustomerReceipt => (0, Credit),
                Bakery.Domain.Constants.LedgerReferenceTypes.PurchaseInvoice => (Credit, Debit),
                Bakery.Domain.Constants.LedgerReferenceTypes.SupplierPayment => (0, Debit),
                null or "" => (Debit + Credit, 0),
                _ => (0, 0)
            };
        }
    }
}
