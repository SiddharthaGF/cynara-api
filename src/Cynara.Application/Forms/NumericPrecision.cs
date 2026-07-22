using System.Globalization;

namespace Cynara.Application.Forms;

internal static class NumericPrecision
{
    private const int MaxDecimalPlaces = 10;
    private const int DefaultDecimalPlaces = 2;

    public static object? NormalizeCalculatedValue(
        object? value,
        ClinicalFieldIndex.FieldInfo field)
    {
        if (value is null)
        {
            return null;
        }

        if (value is not double and not float and not int and not long and not decimal)
        {
            return value;
        }

        double numeric = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        if (!double.IsFinite(numeric))
        {
            return null;
        }

        if (field.Type == "integer")
        {
            return Math.Round(numeric, MidpointRounding.AwayFromZero);
        }

        if (field.Type != "number")
        {
            return value;
        }

        if (field.MultipleOf is double step && step > 0)
        {
            numeric = SnapToStep(numeric, step);
        }

        int decimals = ResolveDecimalPlaces(field.DecimalPlaces, field.MultipleOf);
        return RoundToDecimals(numeric, decimals);
    }

    private static int ResolveDecimalPlaces(int? decimalPlaces, double? multipleOf)
    {
        if (decimalPlaces is int explicitPlaces)
        {
            return ClampDecimalPlaces(explicitPlaces);
        }

        return multipleOf is double step && step > 0 ? DecimalPlacesFromStep(step) : DefaultDecimalPlaces;
    }

    private static int ClampDecimalPlaces(int decimals)
    {
        return Math.Min(MaxDecimalPlaces, Math.Max(0, decimals));
    }

    private static double RoundToDecimals(double value, int decimals)
    {
        decimals = ClampDecimalPlaces(decimals);
        if (decimals <= 0)
        {
            return Math.Round(value, MidpointRounding.AwayFromZero);
        }

        double factor = Math.Pow(10, decimals);
        return Math.Round(value * factor, MidpointRounding.AwayFromZero) / factor;
    }

    private static double SnapToStep(double value, double step)
    {
        if (!double.IsFinite(value) || !double.IsFinite(step) || step <= 0)
        {
            return value;
        }

        int decimals = DecimalPlacesFromStep(step);
        double factor = Math.Pow(10, decimals);
        long scaledStep = (long)Math.Round(step * factor, MidpointRounding.AwayFromZero);
        long scaledValue = (long)Math.Round(value * factor, MidpointRounding.AwayFromZero);
        long snapped = (long)Math.Round((double)scaledValue / scaledStep, MidpointRounding.AwayFromZero) * scaledStep;
        return snapped / factor;
    }

    private static int DecimalPlacesFromStep(double step)
    {
        int decimals = 0;
        double scaled = step;

        while (decimals < MaxDecimalPlaces && Math.Abs(Math.Round(scaled) - scaled) > 1e-9)
        {
            scaled *= 10;
            decimals++;
        }

        return decimals;
    }
}
