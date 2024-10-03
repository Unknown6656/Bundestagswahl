using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bundestagswahl;


internal static class Util
{
    public static DateTime ToDateTime(this DateOnly date) => new(date.Year, date.Month, date.Day);

    public static DateOnly ToDateOnly(this DateTime date) => new(date.Year, date.Month, date.Day);
}
