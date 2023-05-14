using System.Diagnostics;
using Ole32 = Momiji.Interop.Ole32.NativeMethods;

namespace Momiji.Core.Threading;

public class ApartmentType
{
    internal readonly Ole32.APTTYPE AptType;
    internal readonly Ole32.APTTYPEQUALIFIER AptQualifier;
    internal readonly int ManagedThreadId;

    internal ApartmentType(Ole32.APTTYPE aptType, Ole32.APTTYPEQUALIFIER aptQualifier)
    {
        (AptType, AptQualifier, ManagedThreadId) = (aptType, aptQualifier, Environment.CurrentManagedThreadId);
    }

    public bool IsSTA()
    {
        if (AptType == Ole32.APTTYPE.STA || AptType == Ole32.APTTYPE.MAINSTA)
        {
            return true;
        }

        if (AptType == Ole32.APTTYPE.NA)
        {
            if (AptQualifier == Ole32.APTTYPEQUALIFIER.NA_ON_STA || AptQualifier == Ole32.APTTYPEQUALIFIER.NA_ON_MAINSTA)
            {
                return true;
            }
        }
        return false;
    }

    public override string ToString() => $"AptType:{AptType} AptQualifier:{AptQualifier} ManagedThreadId:{ManagedThreadId:X}";

    [ThreadStatic]
    private static ApartmentType? s_apartmentType;

    public static ApartmentType GetApartmentType()
    {
        if (s_apartmentType == null)
        {
            var result = Ole32.CoGetApartmentType(out var pAptType, out var pAptQualifier);
            if (result != 0)
            {
                pAptType = Ole32.APTTYPE.MTA;
                pAptQualifier = Ole32.APTTYPEQUALIFIER.IMPLICIT_MTA;
            }

            s_apartmentType = new ApartmentType(pAptType, pAptQualifier);
        }

        return s_apartmentType;
    }

    [Conditional("DEBUG")]
    public static void CheckNeedMTA()
    {
        Debug.Assert(!GetApartmentType().IsSTA());
    }
}
