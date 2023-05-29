using System.Diagnostics;
using Microsoft.Extensions.Logging;
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

internal static class MTAExecuter
{
    internal static TResult Invoke<TResult>(
        ILogger logger,
        Func<TResult> func
    )
    {
        var apartmentType = ApartmentType.GetApartmentType();

        if (apartmentType.IsSTA())
        {
            logger.LogTrace($"STA {apartmentType}");

            //TODO 一旦、スレッドプールに投げて逃げ。OSのスレッドプールに差し替える意義があれば
            var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.AttachedToParent);
            ThreadPool.QueueUserWorkItem((tcs) => {
                try
                {
                    tcs.SetResult(func());
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            }, tcs, false);

            return tcs.Task.Result;
        }
        else
        {
            logger.LogTrace($"MTA {apartmentType}");
            return func();
        }
    }

    internal static void Invoke(
        ILogger logger,
        Action func
    )
    {
        var apartmentType = ApartmentType.GetApartmentType();

        if (apartmentType.IsSTA())
        {
            logger.LogTrace($"STA {apartmentType}");
            var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent);
            ThreadPool.QueueUserWorkItem((tcs) => {
                try
                {
                    func();
                    tcs.SetResult();
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            }, tcs, false);

            tcs.Task.Wait();
        }
        else
        {
            logger.LogTrace($"MTA {apartmentType}");
            func();
        }
    }
}
