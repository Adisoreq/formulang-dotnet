using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Structures;
using FormuLang.NET.Core.Functions;
using FormuLang.NET.Core.Primitives;
using System.Globalization;
using System.Text;
using static FormuLang.NET.Core.Standards.FunctionHelpers;

namespace FormuLang.NET.Core.Standards
{
    public static partial class CoreFunctions
    {
        /// <summary>
        /// Name of the environment variable with the offset of the local time from UTC, in minutes.
        /// </summary>
        public const string TimeOffsetName = "time_offset";

        private const long MillisecondsPerMinute = 60_000;

        /// <summary>
        /// Time functions (docs/standards/core/functions/README.md, "Time functions"). A point in time is a Number: Unix
        /// time in milliseconds, UTC. The parts are computed in the local time defined by <c>@time_offset</c>.
        /// </summary>
        private static IEnumerable<Function> TimeFunctions() =>
        [
            TimePart("YEAR", t => t.Year),
            TimePart("MONTH", t => t.Month),
            TimePart("DAY", t => t.Day),
            TimePart("WEEKDAY", t => t.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)t.DayOfWeek),
            TimePart("HOUR", t => t.Hour),
            TimePart("MINUTE", t => t.Minute),
            TimePart("SECOND", t => t.Second),
            TimePart("MILLISECOND", t => t.Millisecond),
            Fn("DATE", Overload((a, c) => CreateDate(a, c),
                NumberArg("year"), NumberArg("month"), NumberArg("day"),
                OptionalNumberArg("hour"), OptionalNumberArg("minute"), OptionalNumberArg("second"), OptionalNumberArg("millisecond"))),
            Fn("FORMAT_TIME", Overload((a, c) => FormatTime(Num(a[0]), Txt(a[1]), c), NumberArg("time"), TextArg("format"))),
        ];

        private static ArgumentTemplate OptionalNumberArg(string name) => ArgumentTemplate.Of(name, Types.Number, optional: true);

        /// <summary>
        /// Value of <c>@time_offset</c> in the current execution, in minutes.
        /// </summary>
        private static double TimeOffset(IContext context)
        {
            IObject variables = context.EnvironmentVariables;
            return variables.HasKey(TimeOffsetName) && variables.GetProperty(TimeOffsetName).GetValue().Raw is double offset
                ? offset
                : 0;
        }

        private static Function TimePart(string name, Func<DateTime, int> part)
            => Fn(name, Overload((a, c) => TryLocalTime(Num(a[0]), c, out DateTime local, out IValue? error)
                ? N(part(local))
                : error!, NumberArg("time")));

        /// <summary>
        /// Converts a point in time to the local time of the execution (years 1 - 9999).
        /// </summary>
        private static bool TryLocalTime(double time, IContext context, out DateTime local, out IValue? error)
        {
            local = default;
            error = null;

            if (!IsWhole(time))
            {
                error = OperationError("\"time\" is not a whole number.");
                return false;
            }

            double milliseconds = time + TimeOffset(context) * MillisecondsPerMinute;
            double ticks = DateTime.UnixEpoch.Ticks + milliseconds * TimeSpan.TicksPerMillisecond;

            if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            {
                error = OperationError("The point in time is outside of the supported range (years 1 - 9999).");
                return false;
            }

            local = DateTime.UnixEpoch.AddTicks((long)milliseconds * TimeSpan.TicksPerMillisecond);
            return true;
        }

        private static IValue CreateDate(IReadOnlyList<IValue> arguments, IContext context)
        {
            (string Name, int Min, int Max)[] parts =
            [
                ("year", 1, 9999), ("month", 1, 12), ("day", 1, 31),
                ("hour", 0, 23), ("minute", 0, 59), ("second", 0, 59), ("millisecond", 0, 999),
            ];

            var values = new int[parts.Length];

            for (int i = 0; i < parts.Length; i++)
            {
                double value = i < arguments.Count ? Num(arguments[i]) : 0;

                if (!IsWhole(value))
                    return OperationError($"\"{parts[i].Name}\" is not a whole number.");

                int max = i == 2 && values[0] >= 1 && values[1] >= 1 ? DateTime.DaysInMonth(values[0], values[1]) : parts[i].Max;

                if (value < parts[i].Min || value > max)
                    return OperationError($"\"{parts[i].Name}\" is outside of its range ({parts[i].Min} - {max}).");

                values[i] = (int)value;
            }

            var local = new DateTime(values[0], values[1], values[2], values[3], values[4], values[5], values[6], DateTimeKind.Utc);
            long milliseconds = (local.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerMillisecond;

            return N(milliseconds - TimeOffset(context) * MillisecondsPerMinute);
        }

        /// <summary>
        /// docs/standards/core/functions/FORMAT_TIME.md: the longest token at every position is replaced; text in square
        /// brackets is copied; <c>[[</c> and <c>]]</c> are literal brackets.
        /// </summary>
        private static IValue FormatTime(double time, string format, IContext context)
        {
            if (!TryLocalTime(time, context, out DateTime local, out IValue? error))
                return error!;

            (string Token, Func<DateTime, string> Write)[] tokens =
            [
                ("YYYY", t => t.Year.ToString("D4", CultureInfo.InvariantCulture)),
                ("iii", t => t.Millisecond.ToString("D3", CultureInfo.InvariantCulture)),
                ("MM", t => t.Month.ToString("D2", CultureInfo.InvariantCulture)),
                ("DD", t => t.Day.ToString("D2", CultureInfo.InvariantCulture)),
                ("hh", t => t.Hour.ToString("D2", CultureInfo.InvariantCulture)),
                ("mm", t => t.Minute.ToString("D2", CultureInfo.InvariantCulture)),
                ("ss", t => t.Second.ToString("D2", CultureInfo.InvariantCulture)),
            ];

            var result = new StringBuilder(format.Length + 8);

            for (int i = 0; i < format.Length; i++)
            {
                char c = format[i];
                char? next = i + 1 < format.Length ? format[i + 1] : null;

                if ((c == '[' && next == '[') || (c == ']' && next == ']'))
                {
                    result.Append(c);
                    i++;
                    continue;
                }

                if (c == '[')
                {
                    int end = format.IndexOf(']', i + 1);
                    if (end < 0)
                        return OperationError("The format has a \"[\" without a closing \"]\".");

                    result.Append(format, i + 1, end - i - 1);
                    i = end;
                    continue;
                }

                var match = tokens.FirstOrDefault(t => string.CompareOrdinal(format, i, t.Token, 0, t.Token.Length) == 0);

                if (match.Token != null)
                {
                    result.Append(match.Write(local));
                    i += match.Token.Length - 1;
                }
                else
                    result.Append(c);
            }

            return T(result.ToString());
        }
    }
}
