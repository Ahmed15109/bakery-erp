using Bakery.Application.Interfaces;
using Bakery.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Bakery.Infrastructure.Services;

public sealed class ExceptionTranslator : IExceptionTranslator
{
    public string Translate(Exception ex)
    {
        if (ex is ValidationException vex)
            return vex.Message;

        if (ex is UnauthorizedAccessException or InvalidOperationException)
            return ex.Message;

        if (ex is DbUpdateConcurrencyException)
            return "تم تعديل البيانات بواسطة مستخدم آخر. يرجى إعادة التحميل والمحاولة مرة أخرى.";

        if (ex is DbUpdateException dbEx && dbEx.InnerException is SqlException sqlEx)
        {
            return TranslateSqlException(sqlEx);
        }

        return "حدث خطأ غير متوقع في النظام. يرجى التواصل مع الدعم الفني.";
    }

    public bool IsConcurrencyError(Exception ex) => ex is DbUpdateConcurrencyException;

    private string TranslateSqlException(SqlException ex)
    {
        // Provider text can contain server names, SQL fragments, or connection
        // details. It is logged by the caller, but never returned to the operator.
        if (ex.Number is not (2601 or 2627 or 547))
            return UserErrorMessages.DatabaseOperationFailed;

        // SQL Error Numbers:
        // 2601 - Unique index violation
        // 2627 - Unique constraint violation (Primary Key)
        // 547 - Foreign key violation
        
        switch (ex.Number)
        {
            case 2601:
            case 2627:
                return MapConstraintToMessage(ex.Message);
            case 547:
                return "لا يمكن حذف أو تعديل هذا السجل لوجود بيانات مرتبطة به.";
            default:
                return UserErrorMessages.DatabaseOperationFailed;
        }
    }

    private string MapConstraintToMessage(string message)
    {
        if (message.Contains("IX_Items_Code")) return "كود الصنف مستخدم بالفعل.";
        if (message.Contains("IX_Items_Barcode")) return "الباركود مستخدم بالفعل.";
        if (message.Contains("IX_Users_UserName") || message.Contains("IX_Users_Username")) return "اسم المستخدم موجود مسبقاً.";
        if (message.Contains("IX_Employees_Code")) return "كود الموظف مستخدم بالفعل.";
        if (message.Contains("IX_Safes_Name")) return "اسم الخزنة مستخدم بالفعل.";
        if (message.Contains("IX_WorkingDays_Status") && message.Contains("Open")) 
            return "يوجد بالفعل يوم عمل مفتوح. لا يمكن فتح يومين في نفس الوقت.";
        if (message.Contains("IX_Units_Symbol")) return "رمز الوحدة مستخدم بالفعل.";
        if (message.Contains("IX_WorkingDays_BusinessDate")) return "يوجد بالفعل سجل لهذا التاريخ.";

        return "هذه البيانات موجودة بالفعل (قيد مكرر).";
    }
}
