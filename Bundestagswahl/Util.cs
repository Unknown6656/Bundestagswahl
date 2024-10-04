using System;

namespace Bundestagswahl;


internal static class Util
{
    public static DateTime ToDateTime(this DateOnly date) => new(date.Year, date.Month, date.Day);

    public static DateOnly ToDateOnly(this DateTime date) => new(date.Year, date.Month, date.Day);

    public static int ApproximateBinarySearch<T>(T[] array, T item) where T : IComparable<T> => ApproximateBinarySearch(array, item, 0, array.Length - 1);

    private static int ApproximateBinarySearch<T>(T[] array, T item, int low, int high)
        where T : IComparable<T>
    {
        int diff = high - low;

        if (diff < 2)
            return array[low].CompareTo(item) >= 0 ? low : high;
        else
        {
            int mid = low + (diff / 2);
            (int l, int h) = array[mid].CompareTo(item) >= 0 ? (low, mid) : (mid, high);    

            return ApproximateBinarySearch(array, item, l, h);
        }
    }
}
